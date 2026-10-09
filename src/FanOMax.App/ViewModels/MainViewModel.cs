using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FanOMax.Contracts;
using FanOMax.Core.Analysis;

namespace FanOMax.App.ViewModels;

public sealed record WindowOption(string Label, TimeSpan Duration);

public sealed record StatItem(string Label, string Value);

/// <summary>
/// Tableau de bord en direct. En attendant l'IPC (phase 4), les données viennent des fichiers du service, relus
/// chaque seconde sans droits administrateur : le journal CSV (<c>C:\ProgramData\FanOMax\shadow</c>) pour les courbes,
/// l'instantané <c>live.json</c> pour chaque ventilateur et toutes les températures.
/// </summary>
public partial class MainViewModel : ViewModelBase, IDisposable
{
    /// <summary>Historique gardé en mémoire : la plus grande fenêtre affichable.</summary>
    private static readonly TimeSpan History = TimeSpan.FromHours(6);

    /// <summary>Le service écrit une ligne par seconde : au-delà, il est arrêté (ou la machine sort de veille).</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(10);

    /// <summary>Un groupe sans ligne récente (ancienne configuration, identification des canaux) n'est plus affiché.</summary>
    private static readonly TimeSpan GroupExpiry = TimeSpan.FromMinutes(2);

    private readonly ShadowLogTail _tail;
    private readonly string _livePath;
    private readonly Dictionary<string, List<ShadowRecord>> _history = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _timer;
    private ShadowRecord? _last;
    private LiveSnapshot? _lastLive;
    private bool _reading;
    private bool _updating;

    /// <summary>Dossier du service : <c>C:\ProgramData\FanOMax</c>, ou <c>FANOMAX_HOME</c> comme pour le service (essais).</summary>
    public MainViewModel()
        : this(Path.Combine(
            Environment.GetEnvironmentVariable("FANOMAX_HOME") is { Length: > 0 } custom
                ? custom
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FanOMax"),
            "shadow"))
    {
    }

    public MainViewModel(string directory)
    {
        LogDirectory = directory;
        _livePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(directory)) ?? directory, LiveSnapshot.FileName);
        _tail = new ShadowLogTail(directory, DateOnly.FromDateTime(DateTime.Now - History));
        SelectedWindow = Windows[1];
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => _ = RefreshAsync());
        _timer.Start();
        _ = RefreshAsync();
    }

    /// <summary>Les courbes sont à redessiner (nouvelles données, autre groupe ou autre fenêtre).</summary>
    public event EventHandler? ChartsInvalidated;

    public string LogDirectory { get; }

    public IReadOnlyList<WindowOption> Windows { get; } =
    [
        new("5 min", TimeSpan.FromMinutes(5)),
        new("15 min", TimeSpan.FromMinutes(15)),
        new("1 h", TimeSpan.FromHours(1)),
        new("6 h", TimeSpan.FromHours(6)),
    ];

    public ObservableCollection<GroupViewModel> Groups { get; } = [];

    public ObservableCollection<FanViewModel> Fans { get; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<TemperatureItem> Temperatures { get; set; } = [];

    /// <summary>Message du panneau des ventilateurs (instantané absent ou figé), vide si tout va bien.</summary>
    [ObservableProperty]
    public partial string LiveNotice { get; set; } = "";

    /// <summary>Onglets du panneau des ventilateurs : 0 = en service, 1 = tous les canaux (même vides).</summary>
    public IReadOnlyList<string> FanFilters { get; } = ["En service", "Tous les canaux"];

    [ObservableProperty]
    public partial int FanFilterIndex { get; set; }

    [ObservableProperty]
    public partial GroupViewModel? SelectedGroup { get; set; }

    [ObservableProperty]
    public partial WindowOption SelectedWindow { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<StatItem> Stats { get; set; } = [];

    [ObservableProperty]
    public partial string ModeText { get; set; } = "Lecture du journal…";

    [ObservableProperty]
    public partial string FreshnessText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsPiloting { get; set; }

    [ObservableProperty]
    public partial bool IsStale { get; set; }

    [ObservableProperty]
    public partial bool HasData { get; set; }

    [ObservableProperty]
    public partial string EmptyText { get; set; } = "Lecture du journal du service…";

    /// <summary>Lignes du groupe sélectionné dans la fenêtre affichée, dans l'ordre chronologique.</summary>
    public IReadOnlyList<ShadowRecord> VisibleRows { get; private set; } = [];

    /// <summary>Fin de la fenêtre affichée (heure de la dernière mise à jour).</summary>
    public DateTime Now { get; private set; } = DateTime.Now;

    public void Dispose()
    {
        _timer.Stop();
        GC.SuppressFinalize(this);
    }

    partial void OnSelectedGroupChanged(GroupViewModel? value) => Update();

    partial void OnSelectedWindowChanged(WindowOption value) => Update();

    partial void OnFanFilterIndexChanged(int value) => UpdateLive(_lastLive);

    private async Task RefreshAsync()
    {
        if (_reading)
        {
            return;
        }

        _reading = true;
        try
        {
            // Le premier appel relit tout le fichier du jour (quelques Mo) : hors du fil de l'interface.
            var (records, live) = await Task.Run(() => (_tail.ReadNew(), ReadLive())).ConfigureAwait(true);
            Merge(records);
            UpdateLive(live);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            EmptyText = $"Journal illisible ({LogDirectory}) : {ex.Message}";
        }
        finally
        {
            _reading = false;
        }

        Update();
    }

    private LiveSnapshot? ReadLive()
    {
        try
        {
            // Lecture partagée, y compris en suppression : le service remplace le fichier à chaque cycle.
            using var stream = new FileStream(_livePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return LiveSnapshot.FromJson(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void UpdateLive(LiveSnapshot? live)
    {
        if (live is null)
        {
            // Remplacement en cours ou fichier absent : on garde l'affichage précédent s'il existe.
            if (Fans.Count == 0)
            {
                LiveNotice = "Détail par ventilateur disponible après la mise à jour du service (scripts\\install-service.ps1).";
            }

            return;
        }

        _lastLive = live;
        var age = DateTime.Now - live.Timestamp;
        LiveNotice = age > StaleAfter ? "Valeurs figées depuis " + Duration(age) + " : service arrêté ?" : "";

        // En service : ventilateurs pilotés d'abord. Tous les canaux : ordre de la carte (Fan #1 → #7), GPU à la fin.
        var showAll = FanFilterIndex == 1;
        var shown = live.Fans.Where(f => showAll || FanNames.IsShown(f))
            .OrderBy(f => !showAll && f.Group is null ? 1 : 0)
            .ThenBy(f => f.ControlId.StartsWith("/gpu-", StringComparison.Ordinal) ? 1 : 0)
            .ThenBy(f => f.ControlId, StringComparer.Ordinal)
            .ToList();
        foreach (var gone in Fans.Where(f => shown.All(s => s.ControlId != f.ControlId)).ToList())
        {
            Fans.Remove(gone);
        }

        for (var i = 0; i < shown.Count; i++)
        {
            var fan = Fans.FirstOrDefault(f => f.ControlId == shown[i].ControlId);
            if (fan is null)
            {
                fan = new FanViewModel(shown[i].ControlId);
                Fans.Insert(i, fan);
            }
            else if (Fans.IndexOf(fan) != i)
            {
                Fans.Move(Fans.IndexOf(fan), i);
            }

            fan.Update(shown[i]);
        }

        Temperatures = live.Temperatures
            .Select(TemperatureItem.From)
            .OrderBy(t => t.Rank)
            .ThenBy(t => t.Source, StringComparer.Ordinal)
            .ToList();
    }

    private void Merge(IReadOnlyList<ShadowRecord> records)
    {
        foreach (var record in records)
        {
            if (!_history.TryGetValue(record.Group, out var rows))
            {
                rows = [];
                _history[record.Group] = rows;
            }

            rows.Add(record);
            if (_last is null || record.Time >= _last.Time)
            {
                _last = record;
            }
        }

        var oldest = DateTime.Now - History;
        foreach (var rows in _history.Values)
        {
            var expired = rows.FindIndex(r => r.Time >= oldest);
            rows.RemoveRange(0, expired < 0 ? rows.Count : expired);
        }
    }

    private void Update()
    {
        if (_updating)
        {
            return;
        }

        _updating = true;
        try
        {
            Now = DateTime.Now;
            UpdateHeader();
            UpdateGroups();
            UpdateSelection();
        }
        finally
        {
            _updating = false;
        }

        ChartsInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateHeader()
    {
        var culture = CultureInfo.CurrentCulture;
        HasData = _last is not null;
        if (_last is null)
        {
            ModeText = "Aucune donnée";
            FreshnessText = "";
            IsPiloting = false;
            IsStale = true;
            if (!EmptyText.StartsWith("Journal illisible", StringComparison.Ordinal))
            {
                EmptyText = $"Aucun journal récent dans {LogDirectory}. Le service FanOMax est-il installé et démarré ?";
            }

            return;
        }

        var age = Now - _last.Time;
        IsStale = age > StaleAfter;
        IsPiloting = !IsStale && _last.Writing;
        ModeText = IsStale ? "Service arrêté ?"
            : _last.Writing ? "Pilotage actif"
            : _last.Mode == "Active" ? "Écriture bloquée (FanControl ouvert ?)"
            : "Mode fantôme";
        FreshnessText = IsStale
            ? "Aucune mesure depuis " + Duration(age) + " (dernière : " + _last.Time.ToString("dd/MM HH:mm:ss", culture) + ")"
            : "Dernière mesure " + _last.Time.ToString("HH:mm:ss", culture);
    }

    private void UpdateGroups()
    {
        // Groupes vus récemment par rapport à la dernière mesure (et non à l'heure actuelle : service arrêté, ils restent affichés).
        var reference = _last?.Time ?? Now;
        var active = _history
            .Where(h => h.Value.Count > 0 && reference - h.Value[^1].Time <= GroupExpiry)
            .Select(h => h.Key)
            .OrderBy(Rank)
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var gone in Groups.Where(g => !active.Contains(g.Name)).ToList())
        {
            Groups.Remove(gone);
        }

        for (var i = 0; i < active.Count; i++)
        {
            var group = Groups.FirstOrDefault(g => g.Name == active[i]);
            if (group is null)
            {
                group = new GroupViewModel(active[i]);
                Groups.Insert(i, group);
            }

            group.Update(_history[active[i]][^1]);
        }

        if (SelectedGroup is null || !Groups.Contains(SelectedGroup))
        {
            SelectedGroup = Groups.FirstOrDefault();
        }
    }

    private static int Rank(string name) =>
        name.Equals("CPU", StringComparison.OrdinalIgnoreCase) ? 0
        : name.Equals("GPU", StringComparison.OrdinalIgnoreCase) ? 1
        : 2;

    private void UpdateSelection()
    {
        if (SelectedGroup is null || !_history.TryGetValue(SelectedGroup.Name, out var rows))
        {
            VisibleRows = [];
            Stats = [];
            return;
        }

        var start = Now - SelectedWindow.Duration;
        var first = rows.FindIndex(r => r.Time >= start);
        VisibleRows = first < 0 ? [] : rows.GetRange(first, rows.Count - first);
        Stats = BuildStats(ShadowWindowStats.Compute(VisibleRows));
    }

    private static List<StatItem> BuildStats(ShadowWindowStats stats)
    {
        var culture = CultureInfo.CurrentCulture;
        string F(double? value, string unit, string format = "0.0") => value is { } v ? v.ToString(format, culture) + unit : "—";

        var items = new List<StatItem>
        {
            new("Temp. moyenne", F(stats.Temperature?.Mean, " °C")),
            new("Maximum", F(stats.Temperature?.Max, " °C")),
            new("P95", F(stats.Temperature?.P95, " °C")),
            new("Au-dessus de la cible", stats.Target is null ? "—" : F(stats.AboveTarget, " %", "0")),
            new("Ventilation moyenne", F(stats.Applied?.Mean, " %", "0")),
            new("Course", F(stats.Count >= 2 ? stats.Travel : null, " %/min")),
        };

        foreach (var (status, share) in stats.Statuses.Where(s => s.Status != "Normal"))
        {
            items.Add(new StatItem(StatusText.Label(status), F(share, " % du temps")));
        }

        return items;
    }

    private static string Duration(TimeSpan span) =>
        span.TotalMinutes < 1 ? $"{(int)span.TotalSeconds} s"
        : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min"
        : span.TotalDays < 1 ? $"{(int)span.TotalHours} h {span.Minutes:00}"
        : $"{(int)span.TotalDays} j";
}
