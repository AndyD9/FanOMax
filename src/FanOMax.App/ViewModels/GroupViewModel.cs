using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FanOMax.Core.Analysis;

namespace FanOMax.App.ViewModels;

/// <summary>Carte d'un groupe de ventilateurs : dernières valeurs lues dans le journal du service.</summary>
public partial class GroupViewModel(string name) : ViewModelBase
{
    public string Name { get; } = name;

    [ObservableProperty]
    public partial string Temperature { get; set; } = "—";

    [ObservableProperty]
    public partial string Target { get; set; } = "";

    [ObservableProperty]
    public partial string Power { get; set; } = "—";

    [ObservableProperty]
    public partial string Fan { get; set; } = "—";

    [ObservableProperty]
    public partial string FanDetail { get; set; } = "";

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial bool IsWarning { get; set; }

    [ObservableProperty]
    public partial bool IsAlert { get; set; }

    public void Update(ShadowRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var culture = CultureInfo.CurrentCulture;
        Temperature = record.Temperature is { } t ? t.ToString("0.0", culture) + " °C" : "—";
        Target = record.Target is { } target ? "cible " + target.ToString("0", culture) + " °C" : "";
        Power = record.Power is { } p ? p.ToString("0", culture) + " W" : "—";
        Fan = record.Applied is { } applied ? applied.ToString("0", culture) + " %" : "—";
        FanDetail = record.Decision is { } decision ? "consigne " + decision.ToString("0", culture) + " %" : "";
        Status = StatusText.Label(record.Status);
        IsWarning = StatusText.IsWarning(record.Status);
        IsAlert = StatusText.IsAlert(record.Status);
    }
}

/// <summary>Libellés et niveaux de gravité des états écrits par le service (RegulatorMode, « Critical », « Bios », « Fixed »).</summary>
public static class StatusText
{
    public static string Label(string status) => status switch
    {
        "Normal" => "Normal",
        "TargetUnreachable" => "Cible hors d'atteinte",
        "ThermalLimited" => "Limite thermique",
        "Protection" => "Protection",
        "SensorHolding" => "Capteur instable",
        "SensorLost" => "Capteur perdu",
        "Critical" => "Critique",
        "Bios" => "Rendu au BIOS",
        "Fixed" => "Fixe",
        _ => status,
    };

    public static bool IsWarning(string status) => status is "TargetUnreachable" or "ThermalLimited" or "Protection" or "SensorHolding" or "Fixed";

    public static bool IsAlert(string status) => status is "SensorLost" or "Critical" or "Bios";
}
