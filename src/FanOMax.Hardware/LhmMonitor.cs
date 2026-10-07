using FanOMax.Core.Sensors;
using LibreHardwareMonitor.Hardware;

namespace FanOMax.Hardware;

/// <summary>
/// Accès <b>en lecture seule</b> à LibreHardwareMonitor, utilisé par la sonde.
/// Cette classe n'expose jamais <see cref="IControl"/> : aucune écriture PWM n'est possible à travers elle.
/// Nécessite les droits administrateur et le driver PawnIO.
/// </summary>
public sealed class LhmMonitor : IDisposable
{
    private readonly Computer _computer;
    private readonly List<ISensor> _sensors;
    private bool _disposed;

    private LhmMonitor(Computer computer)
    {
        _computer = computer;

        // Certains capteurs n'apparaissent qu'après une première mise à jour (faite à l'ouverture).
        _sensors = LhmMapping.AllSensors(computer);
        Sensors = _sensors.Select(LhmMapping.ToDescriptor).ToList();
    }

    /// <summary>Capteurs indexés à l'ouverture. L'ordre est stable : il sert d'index de colonnes pour l'enregistrement.</summary>
    public IReadOnlyList<SensorDescriptor> Sensors { get; }

    public static LhmMonitor Open() => new(LhmMapping.OpenComputer());

    /// <summary>Relit tous les capteurs (matériel et sous-matériel).</summary>
    public void Update()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LhmMapping.UpdateAll(_computer);
    }

    /// <summary>Valeurs courantes, dans l'ordre de <see cref="Sensors"/>.</summary>
    public float?[] ReadAll() => _sensors.Select(s => s.Value).ToArray();

    public IReadOnlyList<HardwareNode> GetHardwareTree() => _computer.Hardware.Select(LhmMapping.ToNode).ToList();

    public IReadOnlyList<ControlSnapshot> GetControls() => LhmMapping.GetControls(_computer);

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
}
