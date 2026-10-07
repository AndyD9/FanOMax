using FanOMax.Core.Sensors;

namespace FanOMax.Hardware;

/// <summary>
/// Accès matériel du service : lecture des capteurs et pilotage des sorties PWM.
/// Abstrait pour pouvoir tester le service sans matériel.
/// </summary>
public interface IHardwareBackend : IDisposable
{
    /// <summary>Capteurs indexés à l'ouverture, dans un ordre stable.</summary>
    IReadOnlyList<SensorDescriptor> Sensors { get; }

    /// <summary>Relit tous les capteurs.</summary>
    void Update();

    /// <summary>Valeurs courantes, dans l'ordre de <see cref="Sensors"/>.</summary>
    float?[] ReadAll();

    IReadOnlyList<ControlSnapshot> GetControls();

    /// <summary>Prend le contrôle d'une sortie PWM et lui impose une valeur (0–100 %).</summary>
    void SetControl(string controlId, double percent);

    /// <summary>Rend la sortie PWM à son mode d'origine (courbes du BIOS).</summary>
    void RestoreDefault(string controlId);
}
