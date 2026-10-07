namespace FanOMax.Core.Sensors;

/// <summary>Nature physique d'un capteur. Indépendant de la bibliothèque matérielle utilisée.</summary>
public enum SensorKind
{
    Other,
    Temperature,
    Power,
    Load,
    Fan,
    Control,
    Clock,
    Voltage,
}

public static class SensorKindExtensions
{
    public static string Unit(this SensorKind kind) => kind switch
    {
        SensorKind.Temperature => "°C",
        SensorKind.Power => "W",
        SensorKind.Load => "%",
        SensorKind.Fan => "RPM",
        SensorKind.Control => "%",
        SensorKind.Clock => "MHz",
        SensorKind.Voltage => "V",
        _ => "",
    };
}
