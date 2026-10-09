using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using FanOMax.App.ViewModels;
using FanOMax.Core.Analysis;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.TickGenerators;

namespace FanOMax.App.Views;

public partial class MainWindow : Window
{
    /// <summary>Au-delà de cet écart entre deux lignes (service arrêté, veille), la courbe est interrompue.</summary>
    private static readonly TimeSpan Gap = TimeSpan.FromSeconds(5);

    private const string Hint = "Survolez les courbes pour lire les valeurs à un instant donné.";

    private readonly AvaPlot[] _plots;
    private MainViewModel? _viewModel;
    private double? _cursor;

    public MainWindow()
    {
        InitializeComponent();
        _plots = [TemperaturePlot, FanPlot, PowerPlot];
        foreach (var plot in _plots)
        {
            // Pas de zoom ni de déplacement : la fenêtre de temps se choisit en haut à droite.
            plot.UserInputProcessor.Disable();
            plot.PointerMoved += OnPointerMoved;
            plot.PointerExited += OnPointerExited;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.ChartsInvalidated -= OnChartsInvalidated;
        }

        _viewModel = DataContext as MainViewModel;
        if (_viewModel is not null)
        {
            _viewModel.ChartsInvalidated += OnChartsInvalidated;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel?.Dispose();
        base.OnClosed(e);
    }

    private void OnChartsInvalidated(object? sender, EventArgs e) => Render();

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not AvaPlot plot)
        {
            return;
        }

        var position = e.GetPosition(plot);
        var pixel = new Pixel((float)(position.X * plot.DisplayScale), (float)(position.Y * plot.DisplayScale));
        _cursor = plot.Plot.GetCoordinates(pixel).X;
        Render();
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _cursor = null;
        Render();
    }

    private void Render()
    {
        if (_viewModel is null)
        {
            return;
        }

        var rows = _viewModel.VisibleRows;
        var segments = Segments(rows);
        var end = _viewModel.Now.ToOADate();
        var start = (_viewModel.Now - _viewModel.SelectedWindow.Duration).ToOADate();
        var cursor = Nearest(rows, _cursor);
        // Sur 5 minutes, les graduations tombent toutes les 30 s : les secondes sont utiles.
        var timeFormat = _viewModel.SelectedWindow.Duration <= TimeSpan.FromMinutes(5) ? "HH:mm:ss" : "HH:mm";

        var temperature = Prepare(TemperaturePlot.Plot, timeFormat: null);
        AddStatusSpans(temperature, rows);
        AddSeries(temperature, segments, r => r.Target, PlotTheme.Target, 1.5f, LinePattern.Dashed);
        AddSeries(temperature, segments, r => r.FilteredTemperature, PlotTheme.TemperatureFiltered, 1.5f);
        AddSeries(temperature, segments, r => r.Temperature, PlotTheme.Temperature, 2f);
        SetY(temperature, rows.SelectMany(r => new[] { r.Temperature, r.Target }), minSpan: 12, floor: null);

        var fan = Prepare(FanPlot.Plot, timeFormat: null);
        AddSeries(fan, segments, r => r.Feedforward, PlotTheme.Feedforward, 1.2f, LinePattern.Dotted);
        AddSeries(fan, segments, r => r.Decision, PlotTheme.Decision, 1.5f, LinePattern.Dashed);
        AddSeries(fan, segments, r => r.Applied, PlotTheme.Fan, 2f);
        fan.Axes.SetLimitsY(-3, 103);

        var power = Prepare(PowerPlot.Plot, timeFormat);
        AddSeries(power, segments, r => r.FilteredPower, PlotTheme.PowerFiltered, 1.5f);
        AddSeries(power, segments, r => r.Power, PlotTheme.Power, 2f);
        SetY(power, rows.Select(r => r.Power), minSpan: 20, floor: 0);

        foreach (var plot in _plots)
        {
            if (cursor is not null)
            {
                var line = plot.Plot.Add.VerticalLine(cursor.Time.ToOADate(), 1, PlotTheme.Cursor);
                line.LinePattern = LinePattern.Dotted;
            }

            plot.Plot.Axes.SetLimitsX(start, end);
            plot.Refresh();
        }

        Readout.Text = cursor is null ? Hint : Describe(cursor);
        Readout.Classes.Set("muted", cursor is null);
    }

    /// <param name="timeFormat">Format des heures sous l'axe, ou <c>null</c> pour les masquer (seul le dernier graphique les affiche).</param>
    private static Plot Prepare(Plot plot, string? timeFormat)
    {
        plot.Clear();
        PlotTheme.Apply(plot);
        var culture = CultureInfo.CurrentCulture;
        var axis = plot.Axes.DateTimeTicksBottom();
        axis.TickGenerator = new DateTimeAutomatic { LabelFormatter = d => d.ToString(timeFormat ?? "HH:mm", culture) };
        plot.Axes.Bottom.TickLabelStyle.IsVisible = timeFormat is not null;

        // Marges fixes : les trois graphiques gardent le même axe de temps, aligné au pixel près.
        plot.Layout.Fixed(new PixelPadding(left: 48, right: 14, bottom: timeFormat is null ? 6 : 26, top: 4));
        return plot;
    }

    /// <summary>Découpe la série aux interruptions du journal, pour ne pas relier deux mesures éloignées.</summary>
    private static List<List<ShadowRecord>> Segments(IReadOnlyList<ShadowRecord> rows)
    {
        var segments = new List<List<ShadowRecord>>();
        List<ShadowRecord>? current = null;
        for (var i = 0; i < rows.Count; i++)
        {
            if (current is null || rows[i].Time - rows[i - 1].Time > Gap)
            {
                current = [];
                segments.Add(current);
            }

            current.Add(rows[i]);
        }

        return segments;
    }

    private static void AddSeries(Plot plot, List<List<ShadowRecord>> segments, Func<ShadowRecord, double?> value, Color color, float width, LinePattern? pattern = null)
    {
        foreach (var segment in segments)
        {
            var points = segment.Where(r => value(r).HasValue).ToList();
            if (points.Count == 0)
            {
                continue;
            }

            var xs = points.Select(r => r.Time.ToOADate()).ToArray();
            var ys = points.Select(r => value(r)!.Value).ToArray();
            var scatter = plot.Add.ScatterLine(xs, ys, color);
            scatter.LineWidth = width;
            scatter.LinePattern = pattern ?? LinePattern.Solid;
        }
    }

    /// <summary>Zones colorées : protection, régime limité, alertes (état du régulateur à chaque seconde).</summary>
    private static void AddStatusSpans(Plot plot, IReadOnlyList<ShadowRecord> rows)
    {
        var i = 0;
        while (i < rows.Count)
        {
            var j = i;
            while (j + 1 < rows.Count && rows[j + 1].Status == rows[i].Status && rows[j + 1].Time - rows[j].Time <= Gap)
            {
                j++;
            }

            if (PlotTheme.StatusColor(rows[i].Status) is { } color)
            {
                var span = plot.Add.VerticalSpan(rows[i].Time.ToOADate(), rows[j].Time.AddSeconds(1).ToOADate(), color);
                span.LineStyle.Width = 0;
            }

            i = j + 1;
        }
    }

    private static void SetY(Plot plot, IEnumerable<double?> values, double minSpan, double? floor)
    {
        var valid = values.Where(v => v.HasValue && double.IsFinite(v.Value)).Select(v => v!.Value).ToList();
        if (valid.Count == 0)
        {
            plot.Axes.SetLimitsY(floor ?? 0, (floor ?? 0) + minSpan);
            return;
        }

        var (low, high) = (valid.Min(), valid.Max());
        var padding = Math.Max((minSpan - (high - low)) / 2, 0) + 2;
        low -= padding;
        high += padding;
        if (floor is { } f)
        {
            low = Math.Max(low, f);
        }

        plot.Axes.SetLimitsY(low, high);
    }

    private static ShadowRecord? Nearest(IReadOnlyList<ShadowRecord> rows, double? x)
    {
        if (x is not { } value || rows.Count == 0)
        {
            return null;
        }

        var time = DateTime.FromOADate(value);
        if (time < rows[0].Time - Gap || time > rows[^1].Time + Gap)
        {
            return null;
        }

        var best = rows.MinBy(r => Math.Abs((r.Time - time).Ticks))!;
        return Math.Abs((best.Time - time).TotalSeconds) <= Gap.TotalSeconds ? best : null;
    }

    private static string Describe(ShadowRecord r)
    {
        var c = CultureInfo.CurrentCulture;
        string F(double? v, string unit, string format = "0") => v is { } x ? x.ToString(format, c) + unit : "—";
        return $"{r.Time.ToString("HH:mm:ss", c)}  ·  {F(r.Temperature, " °C", "0.0")} (cible {F(r.Target, " °C")})  ·  "
            + $"ventilation {F(r.Applied, " %")} (consigne {F(r.Decision, " %")})  ·  {F(r.Power, " W")}  ·  {StatusText.Label(r.Status)}";
    }
}
