using FanOMax.Core.Sensors;
using FanOMax.Hardware;

namespace FanOMax.Service.Tests;

/// <summary>
/// Faux matériel reproduisant la machine de référence (5800X, NCT6796D-R, RX 6750 XT) :
/// enregistre chaque écriture et chaque retour au BIOS pour vérifier les règles de sécurité.
/// </summary>
internal sealed class FakeBackend : IHardwareBackend
{
    public const string CpuTemp = "/amdcpu/0/temperature/2";
    public const string CpuPower = "/amdcpu/0/power/0";
    public const string GpuHotSpot = "/gpu-amd/0/temperature/7";
    public const string GpuPower = "/gpu-amd/0/power/3";
    public const string Fan1 = "/lpc/nct6796dr/0/control/0";
    public const string Fan2 = "/lpc/nct6796dr/0/control/1";
    public const string Fan3 = "/lpc/nct6796dr/0/control/2";
    public const string Fan7 = "/lpc/nct6796dr/0/control/6";
    public const string GpuFan = "/gpu-amd/0/control/0";

    private readonly Dictionary<string, float?> _values = new(StringComparer.Ordinal);

    public FakeBackend()
    {
        Sensors =
        [
            new SensorDescriptor(CpuTemp, "Core (Tctl/Tdie)", "AMD Ryzen 7 5800X", SensorKind.Temperature),
            new SensorDescriptor(CpuPower, "Package", "AMD Ryzen 7 5800X", SensorKind.Power),
            new SensorDescriptor(GpuHotSpot, "GPU Hot Spot", "AMD Radeon RX 6750 XT", SensorKind.Temperature),
            new SensorDescriptor(GpuPower, "GPU Package", "AMD Radeon RX 6750 XT", SensorKind.Power),
            new SensorDescriptor(Fan1, "Fan #1", "Nuvoton NCT6796D-R", SensorKind.Control),
            new SensorDescriptor(Fan2, "Fan #2", "Nuvoton NCT6796D-R", SensorKind.Control),
            new SensorDescriptor(Fan3, "Fan #3", "Nuvoton NCT6796D-R", SensorKind.Control),
            new SensorDescriptor(Fan7, "Fan #7", "Nuvoton NCT6796D-R", SensorKind.Control),
            new SensorDescriptor(GpuFan, "GPU Fan", "AMD Radeon RX 6750 XT", SensorKind.Control),
        ];

        Set(CpuTemp, 66);
        Set(CpuPower, 88);
        Set(GpuHotSpot, 62);
        Set(GpuPower, 126);
        foreach (var control in new[] { Fan1, Fan2, Fan3, Fan7 })
        {
            Set(control, 48);
        }

        Set(GpuFan, 58);
    }

    public IReadOnlyList<SensorDescriptor> Sensors { get; }

    /// <summary>Appels SetControl, dans l'ordre.</summary>
    public List<(string Id, double Percent)> Writes { get; } = [];

    /// <summary>Appels RestoreDefault, dans l'ordre.</summary>
    public List<string> Restores { get; } = [];

    /// <summary>Si défini, Update lève cette exception.</summary>
    public Exception? UpdateFailure { get; set; }

    public int UpdateCount { get; private set; }

    public void Set(string id, float? value) => _values[id] = value;

    public void Update()
    {
        UpdateCount++;
        if (UpdateFailure is not null)
        {
            throw UpdateFailure;
        }
    }

    public float?[] ReadAll() => Sensors.Select(s => _values.GetValueOrDefault(s.Id)).ToArray();

    public IReadOnlyList<ControlSnapshot> GetControls() =>
    [
        Control(Fan1, "Fan #1", "Nuvoton NCT6796D-R", 1718),
        Control(Fan2, "Fan #2", "Nuvoton NCT6796D-R", 898),
        Control(Fan3, "Fan #3", "Nuvoton NCT6796D-R", 0),
        Control(Fan7, "Fan #7", "Nuvoton NCT6796D-R", 843),
        Control(GpuFan, "GPU Fan", "AMD Radeon RX 6750 XT", 2850),
    ];

    public void SetControl(string controlId, double percent)
    {
        Writes.Add((controlId, percent));
        _values[controlId] = (float)percent;
    }

    public void RestoreDefault(string controlId) => Restores.Add(controlId);

    public void Dispose()
    {
    }

    private ControlSnapshot Control(string id, string name, string hardware, float rpm) =>
        new(id, name, hardware, "Undefined", _values.GetValueOrDefault(id), null, 0, 100,
            id.Replace("/control/", "/fan/", StringComparison.Ordinal), rpm);
}
