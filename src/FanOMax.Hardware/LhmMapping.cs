using FanOMax.Core.Sensors;
using LibreHardwareMonitor.Hardware;

namespace FanOMax.Hardware;

/// <summary>Code commun aux accès LibreHardwareMonitor (lecture seule et lecture/écriture).</summary>
internal static class LhmMapping
{
    public static Computer OpenComputer()
    {
        var computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
        };

        computer.Open();
        UpdateAll(computer);
        return computer;
    }

    /// <summary>Relit tous les capteurs (matériel et sous-matériel).</summary>
    public static void UpdateAll(Computer computer)
    {
        foreach (var hardware in AllHardware(computer.Hardware))
        {
            hardware.Update();
        }
    }

    public static IEnumerable<IHardware> AllHardware(IEnumerable<IHardware> roots)
    {
        foreach (var hardware in roots)
        {
            yield return hardware;
            foreach (var sub in AllHardware(hardware.SubHardware))
            {
                yield return sub;
            }
        }
    }

    public static List<ISensor> AllSensors(Computer computer) =>
        AllHardware(computer.Hardware).SelectMany(h => h.Sensors).ToList();

    public static List<ControlSnapshot> GetControls(Computer computer)
    {
        var controls = new List<ControlSnapshot>();
        foreach (var hardware in AllHardware(computer.Hardware))
        {
            var fans = hardware.Sensors.Where(s => s.SensorType == SensorType.Fan).ToList();
            foreach (var sensor in hardware.Sensors.Where(s => s.SensorType == SensorType.Control))
            {
                // Sur les puces Super I/O, la sortie PWM n° i pilote en général l'entrée tachymètre n° i.
                var fan = fans.FirstOrDefault(f => f.Index == sensor.Index);
                var control = sensor.Control;
                controls.Add(new ControlSnapshot(
                    Id: sensor.Identifier.ToString(),
                    Name: sensor.Name,
                    HardwareName: hardware.Name,
                    Mode: control?.ControlMode.ToString() ?? "Indisponible",
                    CurrentPercent: sensor.Value,
                    SoftwareValue: control?.SoftwareValue,
                    MinSoftwareValue: control?.MinSoftwareValue ?? 0,
                    MaxSoftwareValue: control?.MaxSoftwareValue ?? 0,
                    PairedFanId: fan?.Identifier.ToString(),
                    PairedFanRpm: fan?.Value));
            }
        }

        return controls;
    }

    public static HardwareNode ToNode(IHardware hardware) => new(
        hardware.Identifier.ToString(),
        hardware.Name,
        hardware.HardwareType.ToString(),
        hardware.Sensors.Select(s => new SensorSnapshot(ToDescriptor(s), s.Value, s.Min, s.Max)).ToList(),
        hardware.SubHardware.Select(ToNode).ToList());

    public static SensorDescriptor ToDescriptor(ISensor sensor) => new(
        sensor.Identifier.ToString(),
        sensor.Name,
        sensor.Hardware.Name,
        ToKind(sensor.SensorType));

    private static SensorKind ToKind(SensorType type) => type switch
    {
        SensorType.Temperature => SensorKind.Temperature,
        SensorType.Power => SensorKind.Power,
        SensorType.Load => SensorKind.Load,
        SensorType.Fan => SensorKind.Fan,
        SensorType.Control => SensorKind.Control,
        SensorType.Clock => SensorKind.Clock,
        SensorType.Voltage => SensorKind.Voltage,
        _ => SensorKind.Other,
    };
}
