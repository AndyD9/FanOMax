using System.Diagnostics;
using System.Globalization;
using FanOMax.Hardware;
using FanOMax.Service.Configuration;
using FanOMax.Service.Engine;
using Microsoft.Extensions.Options;

namespace FanOMax.Service;

/// <summary>
/// Service Windows : boucle de régulation à intervalle fixe, watchdog, rechargement de la configuration,
/// arrêt propre (retour au BIOS des sorties pilotées).
/// </summary>
public sealed partial class RegulationService(
    IOptionsMonitor<FanOMaxOptions> options,
    IConfiguration configuration,
    IHostApplicationLifetime lifetime,
    ILogger<RegulationService> logger,
    ILoggerFactory loggerFactory) : BackgroundService
{
    private const int SummaryEverySeconds = 60;

    private long _lastHeartbeat = Stopwatch.GetTimestamp();
    private int _watchdogTripped;
    private volatile FanOMaxOptions? _pendingOptions;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!SystemChecks.IsAdministrator())
        {
            LogNotAdministrator(logger);
            lifetime.StopApplication();
            return;
        }

        if (!SystemChecks.IsPawnIoInstalled())
        {
            LogPawnIoMissing(logger, SystemChecks.PawnIoLibraryPath);
        }

        using var backend = OpenBackend();
        if (backend is null)
        {
            // Code non nul : Windows relance le service (actions de récupération configurées à l'installation).
            Environment.ExitCode = 1;
            lifetime.StopApplication();
            return;
        }

        if (DefaultConfig.WriteIfMissing(FanOMaxPaths.ConfigFile, backend))
        {
            LogDefaultConfigCreated(logger, FanOMaxPaths.ConfigFile);
            (configuration as IConfigurationRoot)?.Reload();
        }

        var current = options.CurrentValue;
        using var shadowLog = new ShadowLog(FanOMaxPaths.ShadowDirectory, current.ShadowLog.RetentionDays);
        using var engine = new RegulationEngine(backend, current, loggerFactory.CreateLogger<RegulationEngine>(), SystemChecks.IsFanControlRunning, shadowLog);
        using var reload = options.OnChange(o => _pendingOptions = o);
        using var watchdogStop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var watchdog = StartWatchdog(engine, watchdogStop.Token);

        try
        {
            await RunLoopAsync(engine, stoppingToken).ConfigureAwait(false);
        }
        finally
        {
            await watchdogStop.CancelAsync().ConfigureAwait(false);
            watchdog.Join(TimeSpan.FromSeconds(3));
            LogStopping(logger);

            // engine.Dispose (via using) rend au BIOS les sorties pilotées par FanOMax.
        }
    }

    private LhmBackend? OpenBackend()
    {
        try
        {
            return LhmBackend.Open();
        }
#pragma warning disable CA1031 // Toute erreur d'ouverture du matériel doit arrêter proprement le service, avec la cause dans le journal.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogBackendFailed(logger, ex);
            return null;
        }
    }

    private async Task RunLoopAsync(RegulationEngine engine, CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(engine.Options.IntervalSeconds);
        var timer = new PeriodicTimer(interval);
        var clock = Stopwatch.StartNew();
        var consecutiveErrors = 0;
        var sinceSummary = 0.0;

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                var dt = clock.Elapsed.TotalSeconds;
                clock.Restart();

                // Réveil après une veille : l'écart n'est pas un vrai pas de temps (il saturerait filtres et intégrale).
                if (LoopTiming.IsSuspendGap(dt, interval.TotalSeconds))
                {
                    Interlocked.Exchange(ref _lastHeartbeat, Stopwatch.GetTimestamp());
                    engine.ResetRegulators(string.Create(CultureInfo.InvariantCulture, $"reprise après une pause de {dt:0} s (mise en veille ?)"));
                    dt = interval.TotalSeconds;
                }

                if (_pendingOptions is { } pending)
                {
                    _pendingOptions = null;
                    if (engine.Reconfigure(pending) && TimeSpan.FromSeconds(pending.IntervalSeconds) != interval)
                    {
                        interval = TimeSpan.FromSeconds(pending.IntervalSeconds);
                        timer.Dispose();
                        timer = new PeriodicTimer(interval);
                    }
                }

                try
                {
                    var tick = engine.Tick(dt);
                    consecutiveErrors = 0;
                    Interlocked.Exchange(ref _lastHeartbeat, Stopwatch.GetTimestamp());
                    if (Interlocked.Exchange(ref _watchdogTripped, 0) == 1)
                    {
                        LogLoopRecovered(logger);
                    }

                    sinceSummary += dt;
                    if (sinceSummary >= SummaryEverySeconds)
                    {
                        sinceSummary = 0;
                        if (logger.IsEnabled(LogLevel.Information))
                        {
                            var summary = Summarize(tick);
                            LogSummary(logger, summary);
                        }
                    }
                }
#pragma warning disable CA1031 // Une erreur de cycle ne doit pas arrêter la boucle : retour au BIOS, puis nouvelle tentative.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    consecutiveErrors++;
                    LogTickFailed(logger, ex, consecutiveErrors, engine.Options.Safety.MaxConsecutiveErrors);
                    engine.HandBackAll("erreur dans la boucle de régulation");
                    if (consecutiveErrors >= engine.Options.Safety.MaxConsecutiveErrors)
                    {
                        LogTooManyErrors(logger);
                        Environment.ExitCode = 1;
                        lifetime.StopApplication();
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt normal du service.
        }
        finally
        {
            timer.Dispose();
        }
    }

    /// <summary>
    /// Watchdog sur un thread dédié : si la boucle ne progresse plus, rend la main au BIOS.
    /// Un thread dédié reste réactif même si le pool de threads est saturé.
    /// </summary>
    private Thread StartWatchdog(RegulationEngine engine, CancellationToken token)
    {
        var thread = new Thread(() =>
        {
            var lastCheck = Stopwatch.GetTimestamp();
            while (!token.WaitHandle.WaitOne(TimeSpan.FromSeconds(1)))
            {
                var now = Stopwatch.GetTimestamp();
                var sinceLastCheck = Stopwatch.GetElapsedTime(lastCheck, now).TotalSeconds;
                lastCheck = now;

                // Le watchdog lui-même n'a pas tourné : le système était en veille, pas la boucle bloquée.
                if (LoopTiming.IsSuspendGap(sinceLastCheck, 1))
                {
                    Interlocked.Exchange(ref _lastHeartbeat, now);
                    continue;
                }

                var stalled = Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastHeartbeat));
                if (stalled.TotalSeconds > engine.Options.Safety.WatchdogSeconds && Interlocked.Exchange(ref _watchdogTripped, 1) == 0)
                {
                    LogWatchdog(logger, stalled.TotalSeconds);
                    engine.HandBackAll(string.Create(CultureInfo.InvariantCulture, $"watchdog : boucle bloquée depuis {stalled.TotalSeconds:0} s"));
                }
            }
        })
        {
            IsBackground = true,
            Name = "FanOMax watchdog",
        };
        thread.Start();
        return thread;
    }

    private static string Summarize(EngineTick tick)
    {
        var mode = tick.WritingAllowed ? "pilotage" : tick.Mode == OperatingMode.Active ? "Active bloqué (FanControl)" : "fantôme";
        var groups = tick.Groups.Select(g => string.Create(CultureInfo.InvariantCulture,
            $"{g.Name} {g.Temperature:0.0} °C {g.Power:0} W → {g.Percent:0} % ({g.Status}), appliqué {g.AppliedPercent:0} %"));
        return $"[{mode}] {string.Join(" | ", groups)}";
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Droits administrateur requis pour accéder aux capteurs : le service s'arrête.")]
    private static partial void LogNotAdministrator(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Driver PawnIO introuvable ({Path}) : capteurs et contrôles de la carte mère probablement absents.")]
    private static partial void LogPawnIoMissing(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Impossible d'ouvrir LibreHardwareMonitor : le service s'arrête.")]
    private static partial void LogBackendFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Configuration par défaut créée : {Path} (mode fantôme).")]
    private static partial void LogDefaultConfigCreated(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Summary}")]
    private static partial void LogSummary(ILogger logger, string summary);

    [LoggerMessage(Level = LogLevel.Error, Message = "Erreur dans la boucle de régulation ({Count}/{Max}) : sorties rendues au BIOS")]
    private static partial void LogTickFailed(ILogger logger, Exception exception, int count, int max);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Trop d'erreurs consécutives : arrêt du service, le BIOS garde la main.")]
    private static partial void LogTooManyErrors(ILogger logger);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Watchdog : boucle de régulation bloquée depuis {Seconds:0} s, retour au BIOS.")]
    private static partial void LogWatchdog(ILogger logger, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La boucle de régulation est repartie après un blocage.")]
    private static partial void LogLoopRecovered(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Arrêt du service FanOMax.")]
    private static partial void LogStopping(ILogger logger);
}
