namespace FanOMax.Core.Sensors;

/// <summary>
/// Description d'un capteur.
/// <paramref name="Id"/> suit le format LibreHardwareMonitor, par exemple <c>/amdcpu/0/temperature/2</c> :
/// le premier segment identifie le matériel, l'avant-dernier le type de capteur.
/// </summary>
public sealed record SensorDescriptor(string Id, string Name, string HardwareName, SensorKind Kind)
{
    public string Unit => Kind.Unit();

    /// <summary>Déduit le type de capteur depuis l'identifiant (utile pour relire un CSV).</summary>
    public static SensorKind KindFromId(string id)
    {
        var segments = id.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return SensorKind.Other;
        }

        return segments[^2] switch
        {
            "temperature" => SensorKind.Temperature,
            "power" => SensorKind.Power,
            "load" => SensorKind.Load,
            "fan" => SensorKind.Fan,
            "control" => SensorKind.Control,
            "clock" => SensorKind.Clock,
            "voltage" => SensorKind.Voltage,
            _ => SensorKind.Other,
        };
    }
}
