using ScottPlot;

namespace FanOMax.App.Views;

/// <summary>Couleurs des courbes, accordées aux ressources de App.axaml (thème sombre).</summary>
internal static class PlotTheme
{
    public static readonly Color Background = Color.FromHex("#1A1D24");
    public static readonly Color Grid = Color.FromHex("#262B35");
    public static readonly Color Axis = Color.FromHex("#8B93A1");
    public static readonly Color Cursor = Color.FromHex("#E6E8EB").WithAlpha(0.6);

    public static readonly Color Temperature = Color.FromHex("#FF8A5B");
    public static readonly Color TemperatureFiltered = Color.FromHex("#FF8A5B").WithAlpha(0.45);
    public static readonly Color Target = Color.FromHex("#9AA4B2");

    public static readonly Color Fan = Color.FromHex("#3FA7FF");
    public static readonly Color Decision = Color.FromHex("#8CCBFF");
    public static readonly Color Feedforward = Color.FromHex("#6E7B8F");

    public static readonly Color Power = Color.FromHex("#C08CFF");
    public static readonly Color PowerFiltered = Color.FromHex("#C08CFF").WithAlpha(0.45);

    public static void Apply(Plot plot)
    {
        plot.FigureBackground.Color = Background;
        plot.DataBackground.Color = Background;
        plot.Axes.Color(Axis);
        plot.Axes.FrameColor(Grid);
        plot.Grid.MajorLineColor = Grid;

        // Légendes dans les titres de MainWindow.axaml : elles ne masquent pas les courbes.
        plot.HideLegend();
    }

    /// <summary>Couleur de zone d'un état du régulateur, ou <c>null</c> pour un état normal.</summary>
    public static Color? StatusColor(string status) => status switch
    {
        "Protection" => Color.FromHex("#FF5A5A").WithAlpha(0.20),
        "ThermalLimited" or "TargetUnreachable" => Color.FromHex("#F5B83D").WithAlpha(0.14),
        "SensorHolding" or "Fixed" => Color.FromHex("#8B93A1").WithAlpha(0.14),
        "SensorLost" or "Critical" or "Bios" => Color.FromHex("#FF2D55").WithAlpha(0.32),
        _ => null,
    };
}
