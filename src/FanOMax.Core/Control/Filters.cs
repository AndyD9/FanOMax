namespace FanOMax.Core.Control;

/// <summary>
/// Moyenne exponentielle définie par une constante de temps,
/// donc indépendante de la période d'échantillonnage.
/// </summary>
public sealed class Ema(double timeConstantSeconds)
{
    public double? Value { get; private set; }

    public double Update(double sample, double dtSeconds)
    {
        if (Value is not { } current || timeConstantSeconds <= 0)
        {
            Value = sample;
            return sample;
        }

        var alpha = 1 - Math.Exp(-dtSeconds / timeConstantSeconds);
        Value = current + (alpha * (sample - current));
        return Value.Value;
    }

    public void Reset() => Value = null;
}

/// <summary>Moyenne glissante sur une fenêtre de temps (par exemple la puissance sur 25 s).</summary>
public sealed class TimeWindowAverage(double windowSeconds)
{
    private readonly Queue<(double Time, double Value)> _samples = new();
    private double _sum;
    private double _now;

    public double? Value => _samples.Count > 0 ? _sum / _samples.Count : null;

    public double Update(double sample, double dtSeconds)
    {
        _now += dtSeconds;
        _samples.Enqueue((_now, sample));
        _sum += sample;

        while (_samples.Count > 1 && _now - _samples.Peek().Time >= windowSeconds)
        {
            _sum -= _samples.Dequeue().Value;
        }

        return _sum / _samples.Count;
    }
}
