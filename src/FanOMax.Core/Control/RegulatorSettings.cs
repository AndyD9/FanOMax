namespace FanOMax.Core.Control;

public enum FanProfile
{
    Silence,
    Normal,
    Perf,
}

/// <summary>
/// Régime « limité thermiquement » : le CPU tient lui-même sa température en réduisant son boost
/// (THM limit de PBO). La ventilation n'agit alors plus sur les degrés mais sur les fréquences.
/// </summary>
/// <param name="LimitTemperature">THM limit du CPU (°C).</param>
/// <param name="Margin">Le régime est détecté à partir de <c>LimitTemperature − Margin</c>.</param>
/// <param name="EnterSeconds">Durée continue au-dessus du seuil avant d'entrer dans le régime.</param>
/// <param name="ExitHysteresis">Sortie du régime quand la température redescend de ce nombre de degrés sous le seuil.</param>
/// <param name="FanPercent">Ventilation appliquée dans ce régime (dépend du profil).</param>
public sealed record ThermalLimitSettings(
    double LimitTemperature = 80,
    double Margin = 3,
    double EnterSeconds = 10,
    double ExitHysteresis = 2,
    double FanPercent = 75);

/// <summary>Réglages d'un régulateur (un groupe de ventilateurs piloté par une température et une puissance).</summary>
public sealed record RegulatorSettings
{
    /// <summary>Température visée (°C).</summary>
    public double TargetTemperature { get; init; } = 67;

    /// <summary>Modèle statique utilisé pour l'anticipation.</summary>
    public StaticThermalModel Model { get; init; } = StaticThermalModel.Ryzen5800XPhase1;

    /// <summary>Fenêtre de moyenne de la puissance pour l'anticipation : ignore les pics de boost.</summary>
    public double PowerWindowSeconds { get; init; } = 25;

    /// <summary>Constante de temps du filtre de température utilisé par le PI.</summary>
    public double TemperatureTimeConstant { get; init; } = 10;

    public PiSettings Pi { get; init; } = new();

    public double MinPercent { get; init; } = 25;

    public double MaxPercent { get; init; } = 100;

    /// <summary>Montée maximale de la consigne (% par seconde).</summary>
    public double MaxRisePerSecond { get; init; } = 5;

    /// <summary>Descente maximale de la consigne (% par seconde) : lente, pour le confort acoustique.</summary>
    public double MaxFallPerSecond { get; init; } = 1;

    /// <summary>Écart minimal pour modifier la consigne (%).</summary>
    public double Deadband { get; init; } = 1;

    /// <summary>Détection du régime limité thermiquement (null : désactivée, par exemple pour le GPU).</summary>
    public ThermalLimitSettings? ThermalLimit { get; init; } = new();

    public SensorGuardSettings TemperatureGuard { get; init; } = new(Min: 1, Max: 115);

    public SensorGuardSettings PowerGuard { get; init; } = new(Min: 0, Max: 500);

    /// <summary>Ventilation appliquée quand la température est perdue (en attendant que le service rende la main au BIOS).</summary>
    public double FailsafePercent { get; init; } = 100;

    /// <summary>
    /// Réglages CPU (5800X + ventirad) par profil :
    /// Silence vise 70 °C et ventile peu en régime limité ; Perf vise 65 °C et ventile à fond pour garder les fréquences.
    /// </summary>
    public static RegulatorSettings Cpu(FanProfile profile) => profile switch
    {
        FanProfile.Silence => new RegulatorSettings { TargetTemperature = 70, ThermalLimit = new ThermalLimitSettings(FanPercent: 50) },
        FanProfile.Perf => new RegulatorSettings { TargetTemperature = 65, ThermalLimit = new ThermalLimitSettings(FanPercent: 100) },
        _ => new RegulatorSettings { TargetTemperature = 67, ThermalLimit = new ThermalLimitSettings(FanPercent: 75) },
    };

    /// <summary>
    /// Réglages GPU (RX 6750 XT) par profil, régulés sur le point chaud (cible utilisateur : 80–85 °C).
    /// Pas de détection de limite thermique : le GPU en est très loin (69 °C max mesuré en jeu).
    /// </summary>
    public static RegulatorSettings Gpu(FanProfile profile) => new()
    {
        TargetTemperature = profile switch
        {
            FanProfile.Silence => 85,
            FanProfile.Perf => 78,
            _ => 82,
        },
        Model = StaticThermalModel.RadeonRx6750XtPhase1,
        MinPercent = 30,
        ThermalLimit = null,
        TemperatureGuard = new SensorGuardSettings(Min: 1, Max: 120),
        PowerGuard = new SensorGuardSettings(Min: 0, Max: 400),
    };
}
