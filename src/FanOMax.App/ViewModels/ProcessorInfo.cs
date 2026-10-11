using System.Globalization;
using FanOMax.Contracts;

namespace FanOMax.App.ViewModels;

/// <summary>Un cœur : fréquence annoncée, fréquence effective (repos compris) et tension demandée (VID).</summary>
public sealed record CoreItem(string Name, string Clock, string Effective, string Voltage);

/// <summary>
/// Fréquences et tensions du processeur, d'après les noms de LibreHardwareMonitor sur Ryzen
/// (« Core #1 », « Core #1 (Effective) », « Cores (Average) », « Core (SVI2 TFN) », « Core #1 VID »…).
/// Un capteur absent (autre processeur) laisse simplement sa ligne de côté.
/// </summary>
public static class ProcessorInfo
{
    private const string EffectiveSuffix = " (Effective)";
    private const string VidSuffix = " VID";

    public static IReadOnlyList<StatItem> Summary(IReadOnlyList<LiveReading> clocks, IReadOnlyList<LiveReading> voltages)
    {
        ArgumentNullException.ThrowIfNull(clocks);
        ArgumentNullException.ThrowIfNull(voltages);
        var items = new List<StatItem>();

        var cores = clocks.Where(c => CoreNumber(c.Name, "") is not null).ToList();
        if (cores.Count > 0)
        {
            items.Add(new StatItem("Fréquence max", Ghz(cores.Max(c => c.Value))));
        }

        Add(items, "Fréquence moyenne", Find(clocks, "Cores (Average)"), Ghz);
        Add(items, "Fréquence effective", Find(clocks, "Cores (Average Effective)"), Ghz);
        Add(items, "Tension cœurs", Find(voltages, "Core (SVI2 TFN)"), Volts);
        Add(items, "Tension SoC", Find(voltages, "SoC (SVI2 TFN)"), Volts);
        return items;
    }

    public static IReadOnlyList<CoreItem> Cores(IReadOnlyList<LiveReading> clocks, IReadOnlyList<LiveReading> voltages)
    {
        ArgumentNullException.ThrowIfNull(clocks);
        ArgumentNullException.ThrowIfNull(voltages);

        return clocks
            .Select(c => (Number: CoreNumber(c.Name, ""), Reading: c))
            .Where(c => c.Number is not null)
            .OrderBy(c => c.Number)
            .Select(c => new CoreItem(
                "Cœur " + c.Number!.Value.ToString(CultureInfo.CurrentCulture),
                Ghz(c.Reading.Value),
                clocks.FirstOrDefault(e => CoreNumber(e.Name, EffectiveSuffix) == c.Number) is { } effective ? Ghz(effective.Value) : "—",
                voltages.FirstOrDefault(v => CoreNumber(v.Name, VidSuffix) == c.Number) is { } vid ? Volts(vid.Value) : "—"))
            .ToList();
    }

    /// <summary>Numéro du cœur dans « Core #n » suivi exactement de <paramref name="suffix"/>, sinon <c>null</c>.</summary>
    private static int? CoreNumber(string name, string suffix)
    {
        const string Prefix = "Core #";
        if (!name.StartsWith(Prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal)
            || name.Length <= Prefix.Length + suffix.Length)
        {
            return null;
        }

        var digits = name.AsSpan(Prefix.Length, name.Length - Prefix.Length - suffix.Length);
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    private static LiveReading? Find(IReadOnlyList<LiveReading> readings, string name) =>
        readings.FirstOrDefault(r => r.Name == name);

    private static void Add(List<StatItem> items, string label, LiveReading? reading, Func<double, string> format)
    {
        if (reading is not null)
        {
            items.Add(new StatItem(label, format(reading.Value)));
        }
    }

    private static string Ghz(double mhz) => (mhz / 1000).ToString("0.00", CultureInfo.CurrentCulture) + " GHz";

    private static string Volts(double volts) => volts.ToString("0.000", CultureInfo.CurrentCulture) + " V";
}
