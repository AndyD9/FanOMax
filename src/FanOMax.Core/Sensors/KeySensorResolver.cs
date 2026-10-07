namespace FanOMax.Core.Sensors;

/// <summary>Capteurs clés utilisés par la régulation et l'affichage.</summary>
public enum KeySensor
{
    CpuTemperature,
    CpuPower,
    CpuLoad,
    GpuTemperature,
    GpuHotSpot,
    GpuPower,
    GpuLoad,
}

/// <summary>
/// Retrouve les capteurs clés parmi tous ceux exposés par LibreHardwareMonitor,
/// à partir des identifiants et des noms (fonctionne aussi sur les en-têtes d'un CSV).
/// </summary>
public static class KeySensorResolver
{
    private static readonly string[] CpuPrefixes = ["/amdcpu/", "/intelcpu/"];
    private static readonly string[] GpuPrefixes = ["/gpu-amd/", "/gpu-nvidia/", "/gpu-intel/"];

    /// <summary>Renvoie l'index du capteur dans <paramref name="sensors"/>, ou -1 s'il est introuvable.</summary>
    public static int Find(IReadOnlyList<(string Id, string Name)> sensors, KeySensor key) => key switch
    {
        // Sur Ryzen, Tctl/Tdie est la température utilisée par le BIOS pour la régulation.
        KeySensor.CpuTemperature => FindBest(sensors, CpuPrefixes, SensorKind.Temperature, ["Tctl", "Package", "Core Max"]),
        KeySensor.CpuPower => FindBest(sensors, CpuPrefixes, SensorKind.Power, ["Package"]),
        KeySensor.CpuLoad => FindBest(sensors, CpuPrefixes, SensorKind.Load, ["CPU Total", "Total"]),
        KeySensor.GpuTemperature => FindBest(sensors, GpuPrefixes, SensorKind.Temperature, ["GPU Core", "Core"]),
        KeySensor.GpuHotSpot => FindBest(sensors, GpuPrefixes, SensorKind.Temperature, ["Hot Spot"], fallbackToFirst: false),
        KeySensor.GpuPower => FindBest(sensors, GpuPrefixes, SensorKind.Power, ["Package", "Total", "Board"]),
        KeySensor.GpuLoad => FindBest(sensors, GpuPrefixes, SensorKind.Load, ["GPU Core", "D3D 3D", "Core"]),
        _ => -1,
    };

    private static int FindBest(
        IReadOnlyList<(string Id, string Name)> sensors,
        string[] prefixes,
        SensorKind kind,
        string[] preferredNames,
        bool fallbackToFirst = true)
    {
        var candidates = Enumerable.Range(0, sensors.Count)
            .Where(i => prefixes.Any(p => sensors[i].Id.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                        && SensorDescriptor.KindFromId(sensors[i].Id) == kind)
            .ToList();

        foreach (var name in preferredNames)
        {
            var match = candidates.FirstOrDefault(i => sensors[i].Name.Contains(name, StringComparison.OrdinalIgnoreCase), -1);
            if (match >= 0)
            {
                return match;
            }
        }

        return fallbackToFirst && candidates.Count > 0 ? candidates[0] : -1;
    }
}
