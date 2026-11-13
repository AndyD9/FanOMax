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

    /// <param name="exact">
    /// Ignore la zone morte : pour une consigne en butée (minimum ou plafond), que la sortie doit rejoindre exactement
    /// (sinon, avec une zone morte de 3 %, la ventilation au repos resterait à 28 % au lieu de 25 %).
    /// </param>
    public double Update(double target, double dtSeconds, bool exact = false)
    {
        if (Current is not { } current)
        {
            Current = target;
            return target;
        }

        if (!exact && Math.Abs(target - current) < deadband)
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
