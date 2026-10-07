using System.Globalization;

namespace FanOMax.Core.Control;

public enum SensorStatus
{
    /// <summary>Valeur valide.</summary>
    Ok,

    /// <summary>Valeur invalide depuis peu : la dernière valeur valide est conservée.</summary>
    Holding,

    /// <summary>Capteur considéré comme perdu : la régulation ne doit plus s'y fier.</summary>
    Lost,
}

/// <param name="Min">Valeur minimale plausible.</param>
/// <param name="Max">Valeur maximale plausible.</param>
/// <param name="HoldSeconds">Durée pendant laquelle une valeur invalide est remplacée par la dernière valeur valide.</param>
/// <param name="FrozenSeconds">Durée au-delà de laquelle une valeur strictement identique est considérée comme figée.</param>
public sealed record SensorGuardSettings(double Min, double Max, double HoldSeconds = 3, double FrozenSeconds = 120);

/// <summary>
/// Rejette les valeurs aberrantes (absentes, hors plage, figées). Un défaut bref est absorbé en conservant
/// la dernière valeur valide ; un défaut prolongé déclare le capteur perdu.
/// </summary>
public sealed class SensorGuard(SensorGuardSettings settings)
{
    private double? _lastGood;
    private double? _lastRaw;
    private double _sinceGood;
    private double _sameFor;

    public SensorStatus Status { get; private set; } = SensorStatus.Ok;

    /// <summary>Raison du dernier rejet, pour le journal.</summary>
    public string? Reason { get; private set; }

    public double? Update(double? raw, double dtSeconds)
    {
        var problem = Validate(raw, dtSeconds);
        if (problem is null)
        {
            _lastGood = raw;
            _sinceGood = 0;
            Status = SensorStatus.Ok;
            Reason = null;
            return raw;
        }

        _sinceGood += dtSeconds;
        Reason = problem;
        if (_lastGood is not null && _sinceGood <= settings.HoldSeconds)
        {
            Status = SensorStatus.Holding;
            return _lastGood;
        }

        Status = SensorStatus.Lost;
        return null;
    }

    private string? Validate(double? raw, double dtSeconds)
    {
        if (raw is not { } value)
        {
            return "valeur absente";
        }

        if (!double.IsFinite(value))
        {
            return "valeur non numérique";
        }

        if (value < settings.Min || value > settings.Max)
        {
            return string.Create(CultureInfo.InvariantCulture, $"hors plage ({value:0.#}, attendu {settings.Min:0.#}–{settings.Max:0.#})");
        }

        // Un capteur vivant fluctue : une valeur strictement identique pendant longtemps signale un capteur figé.
        _sameFor = _lastRaw is { } last && last.Equals(value) ? _sameFor + dtSeconds : 0;
        _lastRaw = value;
        return _sameFor >= settings.FrozenSeconds ? "valeur figée" : null;
    }
}
