namespace FanOMax.Core.Control;

/// <param name="Kp">Gain proportionnel (% de ventilation par °C d'écart).</param>
/// <param name="Ki">Gain intégral (% par °C et par seconde).</param>
/// <param name="Kd">Gain dérivé (% par °C/s), sur la mesure. 0 par défaut : la température du 5800X est trop rapide et bruitée.</param>
/// <param name="IntegralLimit">Borne absolue de la part intégrale (%).</param>
public sealed record PiSettings(double Kp = 2.0, double Ki = 0.05, double Kd = 0, double IntegralLimit = 30);

/// <summary>
/// Correcteur PI(D) autour d'une anticipation (feedforward).
/// L'écart est mesure − consigne : positif quand c'est trop chaud, donc plus de ventilation.
/// Anti-emballement : l'intégrale est bornée, et gelée quand la sortie sature dans le sens de l'écart.
/// </summary>
public sealed class PiController(PiSettings settings)
{
    private double? _lastMeasurement;

    public double Integral { get; private set; }

    public double LastProportional { get; private set; }

    public double LastDerivative { get; private set; }

    /// <param name="freeze">Gèle l'intégrale (par exemple en régime limité thermiquement).</param>
    public double Update(double measurement, double setpoint, double dtSeconds, double feedforward, double outputMin, double outputMax, bool freeze = false)
    {
        var error = measurement - setpoint;
        LastProportional = settings.Kp * error;

        // Dérivée sur la mesure (et non sur l'écart) : pas de coup de fouet quand la consigne change.
        LastDerivative = _lastMeasurement is { } last && dtSeconds > 0 ? settings.Kd * (measurement - last) / dtSeconds : 0;
        _lastMeasurement = measurement;

        var candidate = Math.Clamp(Integral + (settings.Ki * error * dtSeconds), -settings.IntegralLimit, settings.IntegralLimit);
        var unsaturated = feedforward + LastProportional + candidate + LastDerivative;
        var pushesAboveMax = unsaturated > outputMax && error > 0;
        var pushesBelowMin = unsaturated < outputMin && error < 0;

        if (!freeze && !pushesAboveMax && !pushesBelowMin)
        {
            Integral = candidate;
        }

        return Math.Clamp(feedforward + LastProportional + Integral + LastDerivative, outputMin, outputMax);
    }

    public void Reset()
    {
        Integral = 0;
        _lastMeasurement = null;
    }
}
