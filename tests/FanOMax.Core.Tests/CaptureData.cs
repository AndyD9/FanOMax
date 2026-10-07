using System.Globalization;

namespace FanOMax.Core.Tests;

/// <summary>
/// Séries extraites des captures réelles de la phase 1 (Data/*.csv), échantillonnées à 1 Hz :
/// Ryzen 7 5800X + ventirad, ventilation pilotée par FanControl, Hydra actif (THM limit 80 °C).
/// </summary>
internal sealed record CaptureData(
    double[] Time,
    double?[] CpuPower,
    double?[] CpuTemperature,
    double?[] FanPercent,
    double?[] GpuPower,
    double?[] GpuHotSpot,
    double?[] GpuFanPercent)
{
    public const string Game = "jeu";
    public const string Idle = "repos";
    public const string Cinebench = "cinebench-100pct";

    public int Length => Time.Length;

    public static CaptureData Load(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", name + ".csv");
        var rows = File.ReadAllLines(path).Skip(1).Where(l => l.Length > 0).Select(l => l.Split(',')).ToArray();

        double? Parse(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        double?[] Column(int i) => rows.Select(r => i < r.Length ? Parse(r[i]) : null).ToArray();

        return new CaptureData(
            rows.Select(r => Parse(r[0]) ?? 0).ToArray(),
            Column(1),
            Column(2),
            Column(3),
            Column(4),
            Column(5),
            Column(6));
    }
}
