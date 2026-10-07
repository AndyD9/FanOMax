namespace FanOMax.Core.Control;

/// <summary>
/// Mise en forme acoustique de la consigne : vitesse de variation limitée (montée rapide, descente lente)
/// et zone morte pour éviter les micro-variations audibles.
/// </summary>
/// <param name="maxRisePerSecond">Montée maximale (% par seconde).</param>
/// <param name="maxFallPerSecond">Descente maximale (% par seconde).</param>
/// <param name="deadband">Écart minimal (en %) pour modifier la sortie.</param>
public sealed class OutputShaper(double maxRisePerSecond, double maxFallPerSecond, double deadband)
{
    public double? Current { get; private set; }

    public double Update(double target, double dtSeconds)
    {
        if (Current is not { } current)
        {
            Current = target;
            return target;
        }

        if (Math.Abs(target - current) < deadband)
        {
            return current;
        }

        var step = Math.Clamp(target - current, -maxFallPerSecond * dtSeconds, maxRisePerSecond * dtSeconds);
        Current = current + step;
        return Current.Value;
    }

    /// <summary>Saute directement à une valeur (sécurité : passage immédiat à 100 %).</summary>
    public void Force(double value) => Current = value;
}
