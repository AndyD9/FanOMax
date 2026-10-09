using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FanOMax.Contracts;

namespace FanOMax.App.ViewModels;

/// <summary>Un ventilateur (sortie PWM et tachymètre) : vitesse réelle, % appliqué et objectif.</summary>
public partial class FanViewModel(string controlId) : ViewModelBase
{
    public string ControlId { get; } = controlId;

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Detail { get; set; } = "";

    [ObservableProperty]
    public partial string Rpm { get; set; } = "—";

    [ObservableProperty]
    public partial string Percent { get; set; } = "—";

    [ObservableProperty]
    public partial double PercentValue { get; set; }

    [ObservableProperty]
    public partial string Target { get; set; } = "";

    [ObservableProperty]
    public partial double TargetValue { get; set; }

    [ObservableProperty]
    public partial bool HasTarget { get; set; }

    [ObservableProperty]
    public partial string Owner { get; set; } = "";

    [ObservableProperty]
    public partial bool IsPiloted { get; set; }

    public void Update(LiveFan fan)
    {
        ArgumentNullException.ThrowIfNull(fan);
        var culture = CultureInfo.CurrentCulture;
        Name = FanNames.Friendly(fan);
        Detail = fan.Name + " · " + fan.Hardware;
        Rpm = fan.Rpm is { } rpm ? rpm.ToString("N0", culture) + " tr/min" : "pas de tachymètre";
        Percent = fan.Percent is { } p ? p.ToString("0", culture) + " %" : "—";
        PercentValue = fan.Percent ?? 0;
        HasTarget = fan.TargetPercent.HasValue;
        TargetValue = fan.TargetPercent ?? 0;
        IsPiloted = fan.Written;
        Target = fan.TargetPercent is { } t
            ? (fan.Written ? "objectif " : "objectif (non appliqué) ") + t.ToString("0", culture) + " %"
            : "";
        Owner = fan.Group is { } group
            ? (fan.Written ? "FanOMax · groupe " + group : "FanOMax n'écrit pas · groupe " + group)
            : fan.ControlId.StartsWith("/gpu-", StringComparison.Ordinal) ? "Pilote du GPU" : "Courbe du BIOS";
    }
}

/// <summary>Noms parlants des canaux de la machine (identification du 2026-10-09, docs/phase7-bascule.md).</summary>
public static class FanNames
{
    private static readonly Dictionary<string, string> Known = new(StringComparer.Ordinal)
    {
        ["/lpc/nct6796dr/0/control/0"] = "Ventirad CPU",
        ["/lpc/nct6796dr/0/control/1"] = "Boîtier (Fan #2)",
        ["/lpc/nct6796dr/0/control/6"] = "Boîtier (Fan #7)",
        ["/gpu-amd/0/control/0"] = "Carte graphique",
    };

    public static string Friendly(LiveFan fan)
    {
        ArgumentNullException.ThrowIfNull(fan);
        return Known.GetValueOrDefault(fan.ControlId, fan.Name);
    }

    /// <summary>
    /// Sortie affichée : pilotée par un groupe, ou dont le tachymètre tourne. Les canaux vides
    /// (Fan #3 à #6 sur cette carte : 0 tr/min) sont masqués.
    /// </summary>
    public static bool IsShown(LiveFan fan)
    {
        ArgumentNullException.ThrowIfNull(fan);
        return fan.Group is not null || fan.Rpm is > 0;
    }
}

/// <summary>Une température de l'instantané, avec un libellé court du matériel.</summary>
public sealed record TemperatureItem(string Source, string Name, string Value, int Rank)
{
    public static TemperatureItem From(LiveTemperature t)
    {
        ArgumentNullException.ThrowIfNull(t);
        var (source, rank) = t.Id switch
        {
            _ when t.Id.StartsWith("/amdcpu", StringComparison.Ordinal) || t.Id.StartsWith("/intelcpu", StringComparison.Ordinal) => ("CPU", 0),
            _ when t.Id.StartsWith("/gpu-", StringComparison.Ordinal) => ("GPU", 1),
            _ when t.Id.StartsWith("/lpc", StringComparison.Ordinal) => ("Carte mère", 2),
            _ => (t.Hardware, 3),
        };
        return new TemperatureItem(source, t.Name, t.Value.ToString("0.0", CultureInfo.CurrentCulture) + " °C", rank);
    }
}
