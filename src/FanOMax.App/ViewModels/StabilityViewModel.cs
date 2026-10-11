using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FanOMax.App.Diagnostics;
using FanOMax.Core.Analysis;
using FanOMax.Core.Diagnostics;

namespace FanOMax.App.ViewModels;

/// <summary>Un incident affiché : quoi, quand, où (cœur), et les mesures de FanOMax à ce moment.</summary>
public sealed record IncidentItem(string Title, string When, string Detail, string Context);

/// <summary>
/// Carte « Stabilité » : erreurs matérielles WHEA et redémarrages imprévus du journal Windows, replacés dans le journal
/// de FanOMax. C'est le témoin du réglage du Curve Optimizer et du PBO : un cœur trop sous-volté produit d'abord
/// des erreurs processeur corrigées (WHEA 19), parfois des jours avant un plantage.
/// </summary>
public partial class StabilityViewModel : ViewModelBase, IDisposable
{
    private const int Days = 30;
    private const int Shown = 8;

    /// <summary>Le journal Windows change rarement : inutile de le relire à chaque seconde comme le journal du service.</summary>
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(30);

    /// <summary>Erreur WHEA : la mesure retenue doit être celle du moment (une ligne par seconde).</summary>
    private static readonly TimeSpan SameMoment = TimeSpan.FromSeconds(10);

    /// <summary>Redémarrage : l'événement date du redémarrage, la dernière mesure date de l'arrêt (machine éteinte entre les deux).</summary>
    private static readonly TimeSpan BeforeRestart = TimeSpan.FromDays(3);

    private readonly string _logDirectory;
    private readonly Func<int> _coreCount;
    private readonly DispatcherTimer _timer;

    /// <summary>Contexte déjà calculé par incident : les fichiers du journal ne sont relus que pour un nouvel incident.</summary>
    private readonly Dictionary<(DateTime Time, int Id), string> _contexts = [];

    private bool _reading;

    /// <param name="logDirectory">Dossier des journaux du service (<c>shadow</c>).</param>
    /// <param name="coreCount">Nombre de cœurs physiques connus (panneau du processeur), pour situer une erreur sur son cœur.</param>
    public StabilityViewModel(string logDirectory, Func<int> coreCount)
    {
        _logDirectory = logDirectory;
        _coreCount = coreCount;
        _timer = new DispatcherTimer(RefreshEvery, DispatcherPriority.Background, (_, _) => _ = RefreshAsync());
        _timer.Start();
        _ = RefreshAsync();
    }

    [ObservableProperty]
    public partial string Status { get; set; } = "Lecture…";

    [ObservableProperty]
    public partial bool IsAlert { get; set; }

    [ObservableProperty]
    public partial bool IsWarning { get; set; }

    /// <summary>Pastille grise : journal pas encore lu ou illisible.</summary>
    [ObservableProperty]
    public partial bool IsIdle { get; set; } = true;

    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    /// <summary>Explication affichée quand des erreurs processeur sont présentes, vide sinon.</summary>
    [ObservableProperty]
    public partial string Hint { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<IncidentItem> Incidents { get; set; } = [];

    public void Dispose()
    {
        _timer.Stop();
        GC.SuppressFinalize(this);
    }

    private async Task RefreshAsync()
    {
        if (_reading)
        {
            return;
        }

        _reading = true;
        try
        {
            var cores = _coreCount();
            var (events, items) = await Task.Run(() => Load(cores)).ConfigureAwait(true);
            Show(events, items);
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException)
        {
            Status = "Indisponible";
            IsIdle = true;
            IsAlert = IsWarning = false;
            Summary = "Journal Windows illisible : " + ex.Message;
        }
        finally
        {
            _reading = false;
        }
    }

    private (IReadOnlyList<StabilityEvent> Events, List<IncidentItem> Items) Load(int cores)
    {
        var events = SystemEventLog.ReadIncidents(Days);
        var shown = events.Take(Shown).ToList();

        var missing = shown.Where(e => !_contexts.ContainsKey(Key(e))).ToList();
        if (missing.Count > 0)
        {
            var queries = missing
                .Select(e => new ShadowLookupQuery(e.Time, IsRestart(e) ? BeforeRestart : SameMoment))
                .ToList();
            // Pas de verrou : une seule lecture à la fois (_reading).
            var records = ShadowLogLookup.LastBefore(_logDirectory, queries);
            for (var i = 0; i < missing.Count; i++)
            {
                _contexts[Key(missing[i])] = Context(missing[i], records[i]);
            }
        }

        var items = shown.Select(e => new IncidentItem(
                e.Title,
                e.Time.ToString("dd/MM HH:mm", CultureInfo.CurrentCulture),
                Detail(e, cores),
                _contexts[Key(e)]))
            .ToList();
        return (events, items);
    }

    private void Show(IReadOnlyList<StabilityEvent> events, List<IncidentItem> items)
    {
        var summary = StabilitySummary.Assess(events, DateTime.Now);
        Status = summary.Status;
        IsIdle = false;
        IsAlert = summary.Level == StabilityLevel.Alert;
        IsWarning = summary.Level == StabilityLevel.Warning;
        Summary = events.Count == 0
            ? $"Aucun incident dans le journal Windows ({Days} derniers jours)."
            : $"{Days} derniers jours : {Count(summary.Restarts, "redémarrage imprévu", "redémarrages imprévus")}, "
              + $"{Count(summary.Fatal, "erreur fatale", "erreurs fatales")}, {Count(summary.Corrected, "erreur corrigée", "erreurs corrigées")}"
              + (summary.FailedShutdowns > 0 ? $" ; {Count(summary.FailedShutdowns, "arrêt non terminé", "arrêts non terminés")} (hors plantages)." : ".");
        var hints = new List<string>();
        if (events.Any(e => e.ApicId is not null))
        {
            hints.Add("Avec un Curve Optimizer en place, une erreur processeur signale en général un réglage trop agressif sur ce cœur : le remonter de quelques crans (ex. −20 → −15).");
        }

        if (summary.FailedShutdowns > 0)
        {
            hints.Add("Arrêt non terminé : le PC a été éteint, mais Windows n'a pas fini l'arrêt avec le démarrage rapide (journal : « arrêt non prévu »). "
                      + "Sans lien avec le réglage du processeur ; pour l'éviter, désactiver le démarrage rapide.");
        }

        Hint = string.Join("\n", hints);
        Incidents = items;
    }

    private static (DateTime, int) Key(StabilityEvent e) => (e.Time, e.EventId);

    /// <summary>Événement daté du redémarrage suivant : la mesure utile est la dernière avant l'arrêt.</summary>
    private static bool IsRestart(StabilityEvent e) => e.Kind is StabilityEventKind.UnexpectedRestart or StabilityEventKind.FailedShutdown;

    private static string Detail(StabilityEvent e, int cores)
    {
        var culture = CultureInfo.CurrentCulture;
        if (e.Kind == StabilityEventKind.FailedShutdown)
        {
            return "Extinction demandée, terminée par une coupure (démarrage rapide)";
        }

        if (e.Kind == StabilityEventKind.UnexpectedRestart)
        {
            return e.BugcheckCode is null or 0
                ? "Arrêt brutal, sans écran bleu"
                : "Écran bleu 0x" + e.BugcheckCode.Value.ToString("X", CultureInfo.InvariantCulture);
        }

        var parts = new List<string>();
        if (e.ApicId is { } apic)
        {
            if (StabilityEvents.CoreFromApicId(apic, Environment.ProcessorCount, cores) is { } core)
            {
                parts.Add("Cœur " + core.ToString(culture));
            }

            parts.Add("APIC " + apic.ToString(culture));
        }

        if (e.McaBank is { } bank)
        {
            parts.Add("banc MCA " + bank.ToString(culture));
        }

        parts.Add("WHEA " + e.EventId.ToString(culture));
        return string.Join(" · ", parts);
    }

    private static string Context(StabilityEvent e, IReadOnlyList<ShadowRecord> records)
    {
        if (records.Count == 0)
        {
            return "Aucune mesure de FanOMax à ce moment";
        }

        var culture = CultureInfo.CurrentCulture;
        var measures = string.Join(" · ", records.Select(r =>
            r.Group
            + (r.Temperature is { } t ? " " + t.ToString("0", culture) + " °C" : "")
            + (r.Power is { } p ? ", " + p.ToString("0", culture) + " W" : "")));

        return IsRestart(e)
            ? "Dernière mesure " + records.Max(r => r.Time).ToString("dd/MM HH:mm:ss", culture) + " : " + measures
            : "À ce moment : " + measures;
    }

    private static string Count(int count, string one, string many) =>
        count.ToString(CultureInfo.CurrentCulture) + " " + (count > 1 ? many : one);
}
