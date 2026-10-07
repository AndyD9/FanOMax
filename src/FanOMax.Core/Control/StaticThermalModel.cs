namespace FanOMax.Core.Control;

/// <summary>
/// Modèle thermique statique : <c>T ≈ Intercept + PowerGain·P − FanGain·Ventilo%</c>.
/// Mesuré en phase 1 : la température du 5800X suit la puissance en quelques secondes,
/// donc la ventilation nécessaire se calcule directement depuis la puissance (anticipation).
/// </summary>
/// <param name="Intercept">Température à puissance et ventilation nulles (°C, valeur d'ajustement).</param>
/// <param name="PowerGain">°C par watt.</param>
/// <param name="FanGain">°C gagnés par % de ventilation (positif).</param>
public sealed record StaticThermalModel(double Intercept, double PowerGain, double FanGain)
{
    /// <summary>Ryzen 7 5800X + ventirad, calibré sur les captures de la phase 1 (RMS 1,9 °C).</summary>
    public static StaticThermalModel Ryzen5800XPhase1 { get; } = new(36.8, 0.447, 0.153);

    /// <summary>
    /// Radeon RX 6750 XT, point chaud, calibré sur la capture de jeu de la phase 1 (18 fenêtres stables, RMS 0,9 °C,
    /// ventilation entre 30 et 80 %). À reconfirmer : peu de points, et aucune donnée au-delà de 165 W.
    /// </summary>
    public static StaticThermalModel RadeonRx6750XtPhase1 { get; } = new(44.3, 0.178, 0.103);

    public double PredictTemperature(double power, double fanPercent) =>
        Intercept + (PowerGain * power) - (FanGain * fanPercent);

    /// <summary>Ventilation nécessaire pour tenir <paramref name="target"/> à cette puissance (non bornée).</summary>
    public double RequiredFan(double power, double target) =>
        FanGain > 0 ? (Intercept + (PowerGain * power) - target) / FanGain : double.NaN;

    /// <summary>Puissance maximale tenable à <paramref name="target"/> avec cette ventilation.</summary>
    public double MaxPowerFor(double target, double fanPercent) =>
        PowerGain > 0 ? (target - Intercept + (FanGain * fanPercent)) / PowerGain : double.PositiveInfinity;
}
