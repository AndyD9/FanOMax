using System.Globalization;

namespace FanOMax.Core.Diagnostics;

/// <summary>
/// Gravité d'un incident : erreur corrigée par le matériel, erreur fatale, redémarrage imprévu,
/// ou arrêt demandé que Windows n'a pas terminé (démarrage rapide), sans lien avec la stabilité du processeur.
/// </summary>
public enum StabilityEventKind
{
    CorrectedError,
    FatalError,
    UnexpectedRestart,
    FailedShutdown,
}

/// <summary>
/// Un événement brut du journal Système de Windows, indépendant de l'API de lecture (testable hors Windows).
/// </summary>
/// <param name="Level">Niveau Windows : 1 critique, 2 erreur, 3 avertissement, 4 information.</param>
/// <param name="Data">Champs nommés de <c>EventData</c> (ex. <c>ApicId</c>, <c>BugcheckCode</c>).</param>
public sealed record SystemEventRecord(DateTime Time, string Provider, int Id, int Level, IReadOnlyDictionary<string, string> Data);

/// <summary>Un incident de stabilité retenu pour le tableau de bord.</summary>
/// <param name="ApicId">Processeur logique en cause (erreurs processeur WHEA), sinon <c>null</c>.</param>
/// <param name="McaBank">Banc MCA de l'erreur (erreurs processeur WHEA), sinon <c>null</c>.</param>
/// <param name="BugcheckCode">Code d'écran bleu (redémarrage imprévu) ; 0 = coupure sans écran bleu.</param>
public sealed record StabilityEvent(
    DateTime Time,
    StabilityEventKind Kind,
    string Title,
    int EventId,
    int? ApicId = null,
    int? McaBank = null,
    long? BugcheckCode = null);

/// <summary>
/// Tri des événements du journal Système utiles au réglage du Curve Optimizer et du PBO :
/// les erreurs matérielles WHEA (un undervolt trop agressif produit d'abord des erreurs processeur corrigées,
/// événement 19) et les redémarrages imprévus (Kernel-Power 41).
/// </summary>
public static class StabilityEvents
{
    public const string WheaProvider = "Microsoft-Windows-WHEA-Logger";
    public const string KernelPowerProvider = "Microsoft-Windows-Kernel-Power";
    public const int UnexpectedRestartId = 41;
    public const string KernelBootProvider = "Microsoft-Windows-Kernel-Boot";
    public const int FastStartupFailedId = 29;

    /// <summary>
    /// Écart maximal entre l'échec du démarrage rapide et le Kernel-Power 41 d'un même démarrage
    /// (mesuré le 2026-10-10 et le 2026-10-11 : 2 et 3 s).
    /// </summary>
    private static readonly TimeSpan SameBoot = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Requête XPath du journal Système des <paramref name="days"/> derniers jours : événements WHEA, Kernel-Power 41,
    /// et échecs du démarrage rapide (Kernel-Boot 29, pour reconnaître les arrêts ratés).
    /// </summary>
    public static string Query(int days) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"*[System[(Provider[@Name='{WheaProvider}'] or (Provider[@Name='{KernelPowerProvider}'] and EventID={UnexpectedRestartId}) or (Provider[@Name='{KernelBootProvider}'] and EventID={FastStartupFailedId})) and TimeCreated[timediff(@SystemTime) <= {(long)TimeSpan.FromDays(days).TotalMilliseconds}]]]");

    /// <summary>
    /// Incidents d'une série d'événements, dans le même ordre. Un Kernel-Power 41 sans écran bleu, survenu pendant un arrêt
    /// (<c>SleepInProgress</c> non nul) et suivi d'un échec du démarrage rapide au même démarrage, est un arrêt demandé
    /// que Windows n'a pas terminé : il est classé <see cref="StabilityEventKind.FailedShutdown"/>, pas comme un plantage.
    /// </summary>
    public static IReadOnlyList<StabilityEvent> ClassifyAll(IReadOnlyList<SystemEventRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var fastStartupFailures = records
            .Where(r => r.Provider == KernelBootProvider && r.Id == FastStartupFailedId)
            .Select(r => r.Time)
            .ToList();

        return records
            .Select(r => IsFailedShutdown(r, fastStartupFailures)
                ? new StabilityEvent(r.Time, StabilityEventKind.FailedShutdown, "Arrêt non terminé", r.Id, BugcheckCode: 0)
                : Classify(r))
            .OfType<StabilityEvent>()
            .ToList();
    }

    private static bool IsFailedShutdown(SystemEventRecord record, List<DateTime> fastStartupFailures) =>
        record.Provider == KernelPowerProvider
        && record.Id == UnexpectedRestartId
        && (Number(record.Data, "BugcheckCode") ?? 0) == 0
        && (Number(record.Data, "SleepInProgress") ?? 0) != 0
        && fastStartupFailures.Any(t => (record.Time - t).Duration() <= SameBoot);

    /// <summary>Incident correspondant à l'événement, ou <c>null</c> s'il ne concerne pas la stabilité.</summary>
    public static StabilityEvent? Classify(SystemEventRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (record.Provider == KernelPowerProvider && record.Id == UnexpectedRestartId)
        {
            var code = Number(record.Data, "BugcheckCode") ?? 0;
            return new StabilityEvent(record.Time, StabilityEventKind.UnexpectedRestart, "Redémarrage imprévu", record.Id, BugcheckCode: code);
        }

        if (record.Provider != WheaProvider)
        {
            return null;
        }

        var apic = (int?)Number(record.Data, "ApicId");
        var bank = (int?)Number(record.Data, "MCABank");
        return record.Id switch
        {
            19 => new StabilityEvent(record.Time, StabilityEventKind.CorrectedError, "Erreur processeur corrigée", record.Id, apic, bank),
            18 => new StabilityEvent(record.Time, StabilityEventKind.FatalError, "Erreur processeur fatale", record.Id, apic, bank),
            17 => new StabilityEvent(record.Time, StabilityEventKind.CorrectedError, "Erreur PCI Express corrigée", record.Id),
            47 => new StabilityEvent(record.Time, StabilityEventKind.CorrectedError, "Erreur mémoire corrigée", record.Id),

            // Autres événements WHEA : la gravité suit le niveau Windows (critique ou erreur = fatale).
            _ when record.Level is 1 or 2 => new StabilityEvent(record.Time, StabilityEventKind.FatalError, "Erreur matérielle fatale", record.Id, apic, bank),
            _ when record.Level == 3 => new StabilityEvent(record.Time, StabilityEventKind.CorrectedError, "Erreur matérielle corrigée", record.Id, apic, bank),
            _ => null,
        };
    }

    /// <summary>
    /// Cœur physique (numéroté à partir de 1, comme « Core #n » de LibreHardwareMonitor) d'un identifiant APIC,
    /// ou <c>null</c> si la correspondance n'est pas sûre. Valable pour un CCD complet (5800X : APIC 0 à 15, deux par cœur) ;
    /// sur les processeurs dont les identifiants APIC ont des trous (5900X…), le résultat sort de la plage et vaut <c>null</c>.
    /// </summary>
    public static int? CoreFromApicId(int apicId, int logicalProcessors, int cores)
    {
        if (apicId < 0 || cores <= 0 || logicalProcessors < cores || logicalProcessors % cores != 0)
        {
            return null;
        }

        var core = apicId / (logicalProcessors / cores) + 1;
        return core <= cores ? core : null;
    }

    /// <summary>Champ numérique, écrit en décimal ou en hexadécimal (<c>0x…</c>).</summary>
    private static long? Number(IReadOnlyDictionary<string, string> data, string name)
    {
        if (!data.TryGetValue(name, out var text))
        {
            return null;
        }

        text = text.Trim();
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hex) ? hex : null
            : long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}

/// <summary>Niveau global de la carte « Stabilité ».</summary>
public enum StabilityLevel
{
    Stable,
    Warning,
    Alert,
}

/// <summary>
/// Bilan des incidents récents : un plantage ou une erreur fatale pèse plus qu'une erreur corrigée.
/// Les arrêts non terminés sont comptés sans changer le niveau.
/// </summary>
public sealed record StabilitySummary(StabilityLevel Level, string Status, int Restarts, int Fatal, int Corrected, int FailedShutdowns = 0)
{
    /// <summary>Fenêtre qui fixe la couleur de la pastille : au-delà, les incidents restent listés sans alerter.</summary>
    public static readonly TimeSpan RecentWindow = TimeSpan.FromDays(7);

    public static StabilitySummary Assess(IReadOnlyList<StabilityEvent> events, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(events);

        var recent = events.Where(e => now - e.Time <= RecentWindow).ToList();
        var restarts = events.Count(e => e.Kind == StabilityEventKind.UnexpectedRestart);
        var fatal = events.Count(e => e.Kind == StabilityEventKind.FatalError);
        var corrected = events.Count(e => e.Kind == StabilityEventKind.CorrectedError);
        var failedShutdowns = events.Count(e => e.Kind == StabilityEventKind.FailedShutdown);

        if (recent.Any(e => e.Kind is StabilityEventKind.UnexpectedRestart or StabilityEventKind.FatalError))
        {
            return new StabilitySummary(StabilityLevel.Alert, "Plantage récent", restarts, fatal, corrected, failedShutdowns);
        }

        return recent.Any(e => e.Kind == StabilityEventKind.CorrectedError)
            ? new StabilitySummary(StabilityLevel.Warning, "Erreurs corrigées", restarts, fatal, corrected, failedShutdowns)
            : new StabilitySummary(StabilityLevel.Stable, "Stable", restarts, fatal, corrected, failedShutdowns);
    }
}
