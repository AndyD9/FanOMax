namespace FanOMax.Core.Control;

public enum RegulatorMode
{
    /// <summary>Régulation normale : anticipation + correction PI.</summary>
    Normal,

    /// <summary>La cible n'est pas tenable même à ventilation maximale (puissance trop élevée).</summary>
    TargetUnreachable,

    /// <summary>Le CPU tient lui-même sa température (THM limit) : ventilation selon le profil, PI gelé.</summary>
    ThermalLimited,

    /// <summary>La température monte vraiment (au-delà du plancher de protection) : ventilation minimale imposée.</summary>
    Protection,

    /// <summary>Un capteur vient de renvoyer une valeur invalide : dernière valeur valide conservée.</summary>
    SensorHolding,

    /// <summary>Température perdue : ventilation de sécurité, le service doit rendre la main au BIOS.</summary>
    SensorLost,
}

/// <summary>Décision d'un cycle de régulation, avec le détail utile au journal et à l'interface.</summary>
public readonly record struct RegulatorDecision(
    double Percent,
    RegulatorMode Mode,
    double? FilteredTemperature,
    double? FilteredPower,
    double Feedforward,
    double Correction,
    string? Reason);

/// <summary>
/// Régulateur d'un groupe de ventilateurs : anticipation par modèle statique sur la puissance filtrée,
/// correction PI lente sur la température filtrée, détection du régime limité thermiquement,
/// garde-fous capteurs et mise en forme acoustique de la sortie.
/// </summary>
public sealed class FanRegulator
{
    private readonly RegulatorSettings _settings;
    private readonly SensorGuard _temperatureGuard;
    private readonly SensorGuard _powerGuard;
    private readonly Ema _temperatureFilter;
    private readonly TimeWindowAverage _powerFilter;
    private readonly PiController _pi;
    private readonly OutputShaper _shaper;
    private double _aboveLimitFor;
    private bool _thermalLimited;
    private double _lastFeedforward;
    private double _elapsed;
    private double? _primedPercent;
    private readonly Ema _protectionFilter;
    private readonly FanCurve? _protectionCurve;

    public FanRegulator(RegulatorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _temperatureGuard = new SensorGuard(settings.TemperatureGuard);
        _powerGuard = new SensorGuard(settings.PowerGuard);
        _temperatureFilter = new Ema(settings.TemperatureTimeConstant);
        _powerFilter = new TimeWindowAverage(settings.PowerWindowSeconds);
        _pi = new PiController(settings.Pi);
        _shaper = new OutputShaper(settings.MaxRisePerSecond, settings.MaxFallPerSecond, settings.Deadband);
        _lastFeedforward = settings.MinPercent;
        _protectionFilter = new Ema(settings.ProtectionTimeConstant);
        _protectionCurve = settings.ProtectionCurve is { Count: > 0 } points ? new FanCurve(points) : null;
    }

    public RegulatorSettings Settings => _settings;

    /// <summary>Vrai pendant le préchauffage : la moyenne de puissance n'a pas encore une fenêtre complète.</summary>
    public bool WarmingUp => _elapsed < _settings.PowerWindowSeconds;

    /// <summary>
    /// Démarrage en douceur, à appeler quand FanOMax prend la main sur des ventilateurs qui tournent déjà :
    /// la sortie part de la ventilation actuelle et y reste pendant le préchauffage (moyenne de puissance
    /// pas encore fiable), sauf si la température dépasse déjà la cible de plus de 3 °C.
    /// </summary>
    public void Prime(double currentPercent)
    {
        _primedPercent = Math.Clamp(currentPercent, _settings.MinPercent, _settings.MaxPercent);
        _shaper.Force(_primedPercent.Value);
        _elapsed = 0;
    }

    /// <param name="dtSeconds">Durée écoulée depuis le cycle précédent.</param>
    /// <param name="temperature">Température brute (°C), null si indisponible.</param>
    /// <param name="power">Puissance brute (W), null si indisponible.</param>
    public RegulatorDecision Update(double dtSeconds, double? temperature, double? power)
    {
        var warmingUp = WarmingUp;
        _elapsed += dtSeconds;
        var temp = _temperatureGuard.Update(temperature, dtSeconds);
        var watts = _powerGuard.Update(power, dtSeconds);

        if (temp is null)
        {
            // Sécurité : pas de rampe, passage immédiat à la ventilation de secours.
            _shaper.Force(_settings.FailsafePercent);
            return new RegulatorDecision(_settings.FailsafePercent, RegulatorMode.SensorLost, null, _powerFilter.Value, _lastFeedforward, 0,
                $"Température : {_temperatureGuard.Reason}");
        }

        var filteredTemp = _temperatureFilter.Update(temp.Value, dtSeconds);
        double? filteredPower = watts is { } w ? _powerFilter.Update(w, dtSeconds) : _powerFilter.Value;

        // Puissance indisponible : on garde la dernière anticipation, le PI corrige le reste.
        if (filteredPower is { } p)
        {
            var required = _settings.Model.RequiredFan(p, _settings.TargetTemperature);
            _lastFeedforward = double.IsFinite(required) ? Math.Clamp(required, _settings.MinPercent, _settings.MaxPercent) : _lastFeedforward;
        }

        UpdateThermalLimitState(filteredTemp, dtSeconds);

        double target;
        RegulatorMode mode;
        if (_thermalLimited)
        {
            target = _settings.ThermalLimit!.FanPercent;
            _pi.Update(filteredTemp, _settings.TargetTemperature, dtSeconds, _lastFeedforward, _settings.MinPercent, _settings.MaxPercent, freeze: true);
            mode = RegulatorMode.ThermalLimited;
        }
        else if (warmingUp && _primedPercent is { } primed && filteredTemp <= _settings.TargetTemperature + 3)
        {
            // Démarrage en douceur : on garde la ventilation trouvée, PI gelé, le temps que la moyenne de puissance soit fiable.
            _pi.Update(filteredTemp, _settings.TargetTemperature, dtSeconds, _lastFeedforward, _settings.MinPercent, _settings.MaxPercent, freeze: true);
            target = primed;
            mode = RegulatorMode.Normal;
        }
        else
        {
            target = _pi.Update(filteredTemp, _settings.TargetTemperature, dtSeconds, _lastFeedforward, _settings.MinPercent, _settings.MaxPercent);

            // Pendant le préchauffage, l'anticipation repose sur trop peu de valeurs pour conclure à une cible inatteignable.
            var unreachable = !warmingUp && target >= _settings.MaxPercent && filteredTemp > _settings.TargetTemperature + 1;
            mode = unreachable ? RegulatorMode.TargetUnreachable : RegulatorMode.Normal;
        }

        // Plancher de protection, sur une température à peine lissée : réagit en secondes à une vraie montée,
        // là où le PI (lissage 10 s) est volontairement lent. Le profil garde la main en régime limité thermiquement.
        var fastTemp = _protectionFilter.Update(temp.Value, dtSeconds);
        if (_protectionCurve is { } curve && !_thermalLimited && fastTemp >= _settings.ProtectionCurve![0].Temperature)
        {
            var floor = curve.Evaluate(fastTemp);
            if (floor > target)
            {
                target = floor;
                mode = RegulatorMode.Protection;
            }
        }

        string? reason = null;
        if (_temperatureGuard.Status == SensorStatus.Holding || _powerGuard.Status != SensorStatus.Ok)
        {
            mode = mode == RegulatorMode.Normal ? RegulatorMode.SensorHolding : mode;
            reason = _temperatureGuard.Status != SensorStatus.Ok
                ? $"Température : {_temperatureGuard.Reason}"
                : $"Puissance : {_powerGuard.Reason}";
        }

        var output = _shaper.Update(target, dtSeconds);
        return new RegulatorDecision(output, mode, filteredTemp, filteredPower, _lastFeedforward, target - _lastFeedforward, reason);
    }

    private void UpdateThermalLimitState(double filteredTemp, double dtSeconds)
    {
        if (_settings.ThermalLimit is not { } limit)
        {
            return;
        }

        var threshold = limit.LimitTemperature - limit.Margin;
        if (filteredTemp >= threshold)
        {
            _aboveLimitFor += dtSeconds;
            _thermalLimited |= _aboveLimitFor >= limit.EnterSeconds;
        }
        else
        {
            _aboveLimitFor = 0;
            if (filteredTemp < threshold - limit.ExitHysteresis)
            {
                _thermalLimited = false;
            }
        }
    }
}
