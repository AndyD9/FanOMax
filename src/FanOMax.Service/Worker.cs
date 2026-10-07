namespace FanOMax.Service;

/// <summary>
/// Boucle de régulation. Squelette de la phase 0 : la régulation arrive en phase 3.
/// </summary>
public sealed partial class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(logger);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "FanOMax service démarré (squelette, aucune régulation active).")]
    private static partial void LogStarted(ILogger logger);
}
