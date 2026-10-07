using FanOMax.Core.Sensors;

namespace FanOMax.Hardware;

/// <summary>Valeur d'un capteur à un instant donné.</summary>
public sealed record SensorSnapshot(SensorDescriptor Descriptor, float? Value, float? Min, float? Max);

/// <summary>Nœud de l'arbre matériel (carte mère → puce Super I/O, CPU, GPU…).</summary>
public sealed record HardwareNode(
    string Id,
    string Name,
    string Type,
    IReadOnlyList<SensorSnapshot> Sensors,
    IReadOnlyList<HardwareNode> Children);

/// <summary>
/// État d'un contrôle de ventilateur (sortie PWM), en lecture seule.
/// <see cref="PairedFanId"/> est déduit par l'index du canal : à confirmer en phase 7.
/// </summary>
public sealed record ControlSnapshot(
    string Id,
    string Name,
    string HardwareName,
    string Mode,
    float? CurrentPercent,
    float? SoftwareValue,
    float MinSoftwareValue,
    float MaxSoftwareValue,
    string? PairedFanId,
    float? PairedFanRpm);
