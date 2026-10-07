using FanOMax.Core.Sensors;
using LibreHardwareMonitor.Hardware;

namespace FanOMax.Hardware;

/// <summary>
/// Accès <b>en lecture seule</b> à LibreHardwareMonitor.
/// Cette classe n'expose jamais <see cref="IControl"/> : aucune écriture PWM n'est possible à travers elle.
/// Nécessite les droits administrateur et le driver PawnIO.
/// </summary>
public sealed class LhmMonitor : IDisposable
{
    private readonly Computer _computer;
    private readonly List<ISensor> _sensors = [];
    private bool _disposed;

    private LhmMonitor(Computer computer)
    {
        _computer = computer;
    }

    /// <summary>Capteurs indexés à l'ouverture. L'ordre est stable : il sert d'index de colonnes pour l'enregistrement.</summary>
    public IReadOnlyList<SensorDescriptor> Sensors { get; private set; } = [];

    public static LhmMonitor Open()
    {
        var computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
        };

        computer.Open();
        var monitor = new LhmMonitor(computer);

        // Certains capteurs n'apparaissent qu'après une première mise à jour.
        monitor.Update();
        monitor.IndexSensors();
        return monitor;
    }

    /// <summary>Relit tous les capteurs (matériel et sous-matériel).</summary>
    public void Update()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var hardware in _computer.Hardware)
        {
            UpdateRecursive(hardware);
        }
    }

    /// <summary>Valeurs courantes, dans l'ordre de <see cref="Sensors"/>.</summary>
    public float?[] ReadAll()
    {
        var values = new float?[_sensors.Count];
        for (var i = 0; i < _sensors.Count; i++)
        {
            values[i] = _sensors[i].Value;
        }

        return values;
    }

    public IReadOnlyList<HardwareNode> GetHardwareTree() => _computer.Hardware.Select(ToNode).ToList();

    public IReadOnlyList<ControlSnapshot> GetControls()
    {
        var controls = new List<ControlSnapshot>();
        foreach (var hardware in AllHardware(_computer.Hardware))
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

    /// <summary>Rapport texte brut de LibreHardwareMonitor (peut contenir des numéros de série : ne pas publier).</summary>
    public string GetReport() => _computer.GetReport();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _computer.Close();
    }

    private void IndexSensors()
    {
        _sensors.Clear();
        _sensors.AddRange(AllHardware(_computer.Hardware).SelectMany(h => h.Sensors));
        Sensors = _sensors.Select(ToDescriptor).ToList();
    }

    private static void UpdateRecursive(IHardware hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware)
        {
            UpdateRecursive(sub);
        }
    }

    private static IEnumerable<IHardware> AllHardware(IEnumerable<IHardware> roots)
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

    private static HardwareNode ToNode(IHardware hardware) => new(
        hardware.Identifier.ToString(),
        hardware.Name,
        hardware.HardwareType.ToString(),
        hardware.Sensors.Select(s => new SensorSnapshot(ToDescriptor(s), s.Value, s.Min, s.Max)).ToList(),
        hardware.SubHardware.Select(ToNode).ToList());

    private static SensorDescriptor ToDescriptor(ISensor sensor) => new(
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
