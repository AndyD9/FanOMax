namespace FanOMax.Core.Control;

/// <summary>
/// Courbe classique température → % de ventilation, interpolée linéairement entre les points,
/// avec hystérésis à la descente (mode secours et ventilateurs secondaires).
/// </summary>
public sealed class FanCurve
{
    private readonly (double Temperature, double Percent)[] _points;
    private readonly double _hysteresis;
    private double? _heldTemperature;

    public FanCurve(IEnumerable<(double Temperature, double Percent)> points, double hysteresis = 0)
    {
        ArgumentNullException.ThrowIfNull(points);
        _points = points.OrderBy(p => p.Temperature).ToArray();
        if (_points.Length == 0)
        {
            throw new ArgumentException("La courbe doit contenir au moins un point.", nameof(points));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(hysteresis);
        _hysteresis = hysteresis;
    }

    /// <summary>Valeur de la courbe, sans hystérésis.</summary>
    public double Evaluate(double temperature)
    {
        if (temperature <= _points[0].Temperature)
        {
            return _points[0].Percent;
        }

        for (var i = 1; i < _points.Length; i++)
        {
            var (t1, p1) = _points[i];
            if (temperature <= t1)
            {
                var (t0, p0) = _points[i - 1];
                return p0 + ((p1 - p0) * (temperature - t0) / (t1 - t0));
            }
        }

        return _points[^1].Percent;
    }

    /// <summary>
    /// Valeur avec hystérésis : la courbe suit immédiatement une hausse, mais ne redescend
    /// qu'une fois la température tombée d'au moins <c>hysteresis</c> degrés.
    /// </summary>
    public double Update(double temperature)
    {
        if (_heldTemperature is not { } held || temperature > held || held - temperature >= _hysteresis)
        {
            _heldTemperature = temperature;
        }

        return Evaluate(_heldTemperature.Value);
    }
}
