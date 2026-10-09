namespace FanOMax.Service.Engine;

/// <summary>
/// Détection des mises en veille / hibernations : pendant une veille, aucun thread ne tourne,
/// mais l'horloge continue. Au réveil, l'écart entre deux cycles est donc énorme (incident du 2026-10-08 : 83 298 s).
/// </summary>
public static class LoopTiming
{
    /// <summary>Écart minimal (s) considéré comme une pause du système plutôt qu'un cycle lent.</summary>
    public const double MinSuspendGapSeconds = 10;

    /// <summary>
    /// Vrai si l'écart entre deux exécutions attendues toutes les <paramref name="expectedSeconds"/> secondes
    /// ne peut s'expliquer que par une pause du système (veille, hibernation).
    /// </summary>
    public static bool IsSuspendGap(double elapsedSeconds, double expectedSeconds) =>
        elapsedSeconds > Math.Max(MinSuspendGapSeconds, 5 * expectedSeconds);
}
