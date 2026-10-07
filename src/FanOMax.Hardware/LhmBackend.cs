using FanOMax.Core.Sensors;
using LibreHardwareMonitor.Hardware;

namespace FanOMax.Hardware;

/// <summary>
/// Accès LibreHardwareMonitor en lecture et écriture, pour le service.
/// Le service décide quand écrire (jamais en mode fantôme) ; cette classe se contente d'exécuter.
/// </summary>
public sealed class LhmBackend : IHardwareBackend
{
    private readonly Computer _computer;
    private readonly List<ISensor> _sensors;
    private readonly Dictionary<string, IControl> _controls;
    private bool _disposed;

    private LhmBackend(Computer computer)
    {
        _computer = computer;
        _sensors = LhmMapping.AllSensors(computer);
        Sensors = _sensors.Select(LhmMapping.ToDescriptor).ToList();
        _controls = _sensors
            .Where(s => s.SensorType == SensorType.Control && s.Control is not null)
            .ToDictionary(s => s.Identifier.ToString(), s => s.Control!, StringComparer.Ordinal);
    }

    public IReadOnlyList<SensorDescriptor> Sensors { get; }

    public static LhmBackend Open() => new(LhmMapping.OpenComputer());

    public void Update()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LhmMapping.UpdateAll(_computer);
    }

    public float?[] ReadAll() => _sensors.Select(s => s.Value).ToArray();

    public IReadOnlyList<ControlSnapshot> GetControls() => LhmMapping.GetControls(_computer);

    public void SetControl(string controlId, double percent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var control = Find(controlId);
        var value = (float)Math.Clamp(percent, control.MinSoftwareValue, control.MaxSoftwareValue);
        control.SetSoftware(value);
    }

    public void RestoreDefault(string controlId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Find(controlId).SetDefault();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _computer.Close();
    }

    private IControl Find(string controlId) =>
        _controls.TryGetValue(controlId, out var control)
            ? control
            : throw new ArgumentException($"Contrôle PWM inconnu : {controlId}", nameof(controlId));
}
