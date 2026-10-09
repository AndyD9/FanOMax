using System.Collections.Immutable;
using System.Text.Json;
using FanOMax.Core.Control;
using FanOMax.Hardware;
using FanOMax.Service.Configuration;
using Microsoft.Extensions.Logging;

namespace FanOMax.Service.Engine;

/// <summary>
/// Cœur du service : à chaque cycle, lit les capteurs, fait tourner un régulateur par groupe et,
/// <b>seulement en mode Active et en l'absence de FanControl</b>, écrit les consignes.
/// <para>
/// Règle de sécurité : FanOMax ne rend au BIOS que les sorties qu'il a lui-même pilotées. En mode fantôme,
/// il n'appelle jamais ni <see cref="IHardwareBackend.SetControl"/> ni <see cref="IHardwareBackend.RestoreDefault"/>,
/// pour ne jamais retirer la main à FanControl.
/// </para>
/// </summary>
public sealed partial class RegulationEngine : IDisposable
{
    private const double FanControlCheckSeconds = 10;
    private const double ShadowLogRetrySeconds = 60;

    private readonly IHardwareBackend _backend;
    private readonly ILogger _logger;
    private readonly Func<bool> _isFanControlRunning;
    private readonly ShadowLog? _shadowLog;
    private readonly Func<DateTime> _clock;

    // Sorties actuellement pilotées par FanOMax. Ensemble immuable : le watchdog peut le lire depuis un autre thread.
    private ImmutableHashSet<string> _written = ImmutableHashSet.Create<string>(StringComparer.Ordinal);
    private List<GroupRuntime> _groups = [];
    private FanOMaxOptions _options = new();
    private string? _serializedOptions;
    private double _sinceFanControlCheck = double.MaxValue;
    private double _shadowLogRetryIn;
    private bool _fanControlRunning;
    private bool _disposed;

    public RegulationEngine(
        IHardwareBackend backend,
        FanOMaxOptions options,
        ILogger logger,
        Func<bool> isFanControlRunning,
        ShadowLog? shadowLog = null,
        Func<DateTime>? clock = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _isFanControlRunning = isFanControlRunning ?? throw new ArgumentNullException(nameof(isFanControlRunning));
        _shadowLog = shadowLog;
        _clock = clock ?? (() => DateTime.Now);
        Reconfigure(options);
    }

    public FanOMaxOptions Options => _options;

    /// <summary>Erreurs de la dernière configuration (refusée entièrement, ou groupes désactivés).</summary>
    public IReadOnlyList<string> ConfigurationErrors { get; private set; } = [];

    /// <summary>Sorties actuellement pilotées par FanOMax.</summary>
    public IReadOnlySet<string> WrittenControls => _written;

    public IReadOnlyList<string> GroupNames => _groups.Select(g => g.Name).ToList();

    /// <summary>
    /// Applique une nouvelle configuration. Une configuration invalide est refusée et l'actuelle reste active.
    /// Un groupe dont un capteur ou une sortie est introuvable est désactivé (les autres fonctionnent).
    /// </summary>
    /// <returns>Vrai si la configuration a été appliquée.</returns>
    public bool Reconfigure(FanOMaxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Rechargement sans changement réel (l'enregistrement d'un fichier peut déclencher plusieurs notifications) :
        // on garde l'état des régulateurs (filtres, intégrale) au lieu de les remettre à zéro.
        var serialized = JsonSerializer.Serialize(options);
        if (_groups.Count > 0 && serialized == _serializedOptions)
        {
            return true;
        }

        var errors = FanOMaxOptionsValidator.Validate(options);
        if (errors.Count > 0)
        {
            ConfigurationErrors = errors;
            foreach (var error in errors)
            {
                LogConfigurationRejected(_logger, error);
            }

            return false;
        }

        var groups = new List<GroupRuntime>();
        foreach (var groupOptions in options.Groups.Where(g => g.Enabled))
        {
            if (GroupRuntime.Resolve(groupOptions, options.Profile, _backend, out var error) is { } group)
            {
                groups.Add(group);
            }
            else
            {
                errors.Add(error!);
                LogGroupDisabled(_logger, error!);
            }
        }

        // Sorties qui ne sont plus pilotées (groupe retiré, ou passage en mode fantôme) : rendues au BIOS.
        var managed = options.Mode == OperatingMode.Active
            ? groups.SelectMany(g => g.ControlIds).ToHashSet(StringComparer.Ordinal)
            : [];
        foreach (var id in _written.Where(id => !managed.Contains(id)))
        {
            Restore(id, "changement de configuration");
        }

        _groups = groups;
        _options = options;
        _serializedOptions = serialized;
        ConfigurationErrors = errors;
        if (_logger.IsEnabled(LogLevel.Information))
        {
            var description = string.Join(", ", groups.Select(g => $"{g.Name} ({g.ControlIds.Count} sortie(s), cible {g.Settings.TargetTemperature} °C)"));
            LogConfigured(_logger, options.Mode, options.Profile, description);
        }

        return true;
    }

    public EngineTick Tick(double dtSeconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _backend.Update();
        var values = _backend.ReadAll();
        UpdateFanControlState(dtSeconds);

        var writing = _options.Mode == OperatingMode.Active && !_fanControlRunning;
        var results = new List<GroupTick>(_groups.Count);
        foreach (var group in _groups)
        {
            results.Add(TickGroup(group, values, dtSeconds, writing));
        }

        var tick = new EngineTick(_clock(), _options.Mode, writing, _fanControlRunning, results);
        WriteShadowLog(tick, dtSeconds);
        return tick;
    }

    /// <summary>Vrai tant que le journal des décisions est en échec (écriture suspendue).</summary>
    public bool ShadowLogFailing { get; private set; }

    /// <summary>
    /// Repart de régulateurs neufs, par exemple au réveil du PC : l'état accumulé avant la veille
    /// (filtres, intégrale, moyenne de puissance) ne décrit plus la situation.
    /// </summary>
    public void ResetRegulators(string reason)
    {
        foreach (var group in _groups)
        {
            group.ResetRegulator();
        }

        LogRegulatorsReset(_logger, reason);
    }

    /// <summary>
    /// Le journal des décisions est un outil de diagnostic : son échec (disque plein…) ne doit jamais
    /// interrompre la régulation. Erreur journalisée une fois, écriture suspendue puis retentée.
    /// </summary>
    private void WriteShadowLog(EngineTick tick, double dtSeconds)
    {
        if (!_options.ShadowLog.Enabled || _shadowLog is null)
        {
            return;
        }

        if (_shadowLogRetryIn > 0)
        {
            _shadowLogRetryIn -= dtSeconds;
            return;
        }

        try
        {
            _shadowLog.Write(tick);
            if (ShadowLogFailing)
            {
                ShadowLogFailing = false;
                LogShadowLogRestored(_logger);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!ShadowLogFailing)
            {
                LogShadowLogFailed(_logger, ex, ShadowLogRetrySeconds);
            }

            ShadowLogFailing = true;
            _shadowLogRetryIn = ShadowLogRetrySeconds;
        }
    }

    /// <summary>
    /// Rend au BIOS toutes les sorties pilotées par FanOMax. Utilisable depuis un autre thread (watchdog).
    /// </summary>
    public void HandBackAll(string reason)
    {
        var written = Interlocked.Exchange(ref _written, _written.Clear());
        if (written.Count == 0)
        {
            return;
        }

        LogHandBack(_logger, written.Count, reason);
        foreach (var id in written)
        {
            try
            {
                _backend.RestoreDefault(id);
            }
#pragma warning disable CA1031 // Rendre la main au BIOS est best-effort : une sortie en échec ne doit pas empêcher les autres.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogRestoreFailed(_logger, ex, id);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        HandBackAll("arrêt du service");
        _shadowLog?.Dispose();
        _disposed = true;
    }

    private GroupTick TickGroup(GroupRuntime group, float?[] values, double dt, bool writing)
    {
        var temperature = Read(values, group.TemperatureIndex);
        var power = Read(values, group.PowerIndex);
        var appliedValues = group.ControlSensorIndices.Select(i => Read(values, i)).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        double? applied = appliedValues.Count > 0 ? appliedValues.Average() : null;

        var regulatorTemperature = EstimateTemperature(group, temperature, applied, dt, writing);
        var decision = group.Regulator.Update(dt, regulatorTemperature, power);
        group.LastDecisionPercent = decision.Percent;

        var percent = decision.Percent;
        var status = decision.Mode.ToString();
        if (temperature >= group.Options.EffectiveCriticalTemperature)
        {
            percent = 100;
            status = "Critical";
        }

        var written = false;
        if (writing)
        {
            if (decision.Mode == RegulatorMode.SensorLost)
            {
                if (!group.HandedBack)
                {
                    foreach (var id in group.ControlIds)
                    {
                        Restore(id, $"groupe {group.Name} : {decision.Reason}");
                    }

                    group.HandedBack = true;
                }

                group.RecoveredFor = 0;
            }
            else if (group.HandedBack)
            {
                group.RecoveredFor += dt;
                if (group.RecoveredFor >= _options.Safety.ResumeAfterSeconds)
                {
                    group.HandedBack = false;
                    LogResumed(_logger, group.Name);
                }
            }

            if (group.HandedBack)
            {
                status = "Bios";
            }
            else
            {
                foreach (var id in group.ControlIds)
                {
                    _backend.SetControl(id, percent);
                    ImmutableInterlocked.Update(ref _written, set => set.Add(id));
                }

                written = true;
            }
        }

        if (status != group.LastStatus)
        {
            LogStatusChanged(_logger, group.Name, group.LastStatus ?? "—", status, decision.Reason ?? "");
            group.LastStatus = status;
        }

        return new GroupTick(
            group.Name,
            group.Settings.TargetTemperature,
            temperature,
            regulatorTemperature,
            power,
            decision,
            percent,
            status,
            applied,
            written);
    }

    /// <summary>
    /// Température transmise au régulateur. Quand FanOMax pilote, c'est la mesure.
    /// <para>
    /// Quand il ne pilote pas (mode fantôme, FanControl présent), la température mesurée résulte de la ventilation
    /// de FanControl, pas de celle de FanOMax : sans correction, la boucle est ouverte et le PI s'accumule jusqu'à la butée.
    /// On estime donc la température qu'aurait produite la ventilation de FanOMax, via l'effet des ventilateurs du modèle,
    /// avec un retard qui imite l'inertie du ventirad.
    /// </para>
    /// </summary>
    private static double? EstimateTemperature(GroupRuntime group, double? measured, double? applied, double dt, bool writing)
    {
        if (writing || measured is not { } t || applied is not { } a || group.LastDecisionPercent is not { } decided)
        {
            group.ShadowOffset.Reset();
            return measured;
        }

        var offset = group.Settings.Model.FanGain * (a - decided);
        return t + group.ShadowOffset.Update(offset, dt);
    }

    private void UpdateFanControlState(double dt)
    {
        _sinceFanControlCheck += dt;
        if (_sinceFanControlCheck < FanControlCheckSeconds)
        {
            return;
        }

        _sinceFanControlCheck = 0;
        var running = _isFanControlRunning();
        if (running == _fanControlRunning)
        {
            return;
        }

        _fanControlRunning = running;
        if (running && _options.Mode == OperatingMode.Active)
        {
            // FanControl pilote ces sorties : on cesse d'écrire, sans les rendre au BIOS (ce serait lui retirer la main).
            Interlocked.Exchange(ref _written, _written.Clear());
            LogFanControlDetected(_logger);
        }
        else if (!running && _options.Mode == OperatingMode.Active)
        {
            LogFanControlGone(_logger);
        }
    }

    private void Restore(string id, string reason)
    {
        if (!_written.Contains(id))
        {
            return;
        }

        ImmutableInterlocked.Update(ref _written, set => set.Remove(id));
        LogRestore(_logger, id, reason);
        _backend.RestoreDefault(id);
    }

    private static double? Read(float?[] values, int index) =>
        index >= 0 && index < values.Length && values[index] is { } v ? v : null;

    [LoggerMessage(Level = LogLevel.Information, Message = "Configuration appliquée : mode {Mode}, profil {Profile}, groupes : {Groups}")]
    private static partial void LogConfigured(ILogger logger, OperatingMode mode, FanProfile profile, string groups);

    [LoggerMessage(Level = LogLevel.Error, Message = "Configuration refusée (la précédente reste active) : {Error}")]
    private static partial void LogConfigurationRejected(ILogger logger, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Groupe désactivé : {Error}")]
    private static partial void LogGroupDisabled(ILogger logger, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Groupe {Group} : {Previous} → {Status} {Reason}")]
    private static partial void LogStatusChanged(ILogger logger, string group, string previous, string status, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retour au BIOS de {Count} sortie(s) PWM : {Reason}")]
    private static partial void LogHandBack(ILogger logger, int count, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sortie {Control} rendue au BIOS : {Reason}")]
    private static partial void LogRestore(ILogger logger, string control, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Impossible de rendre la sortie {Control} au BIOS")]
    private static partial void LogRestoreFailed(ILogger logger, Exception exception, string control);

    [LoggerMessage(Level = LogLevel.Information, Message = "Groupe {Group} : capteurs de nouveau valides, FanOMax reprend la main")]
    private static partial void LogResumed(ILogger logger, string group);

    [LoggerMessage(Level = LogLevel.Warning, Message = "FanControl est en cours d'exécution : FanOMax n'écrit plus et lui laisse la main")]
    private static partial void LogFanControlDetected(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "FanControl n'est plus en cours d'exécution : FanOMax pilote les ventilateurs")]
    private static partial void LogFanControlGone(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Journal des décisions en échec (la régulation continue) : nouvel essai dans {RetrySeconds} s")]
    private static partial void LogShadowLogFailed(ILogger logger, Exception exception, double retrySeconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Journal des décisions de nouveau écrit")]
    private static partial void LogShadowLogRestored(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Régulateurs réinitialisés : {Reason}")]
    private static partial void LogRegulatorsReset(ILogger logger, string reason);
}
