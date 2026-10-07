namespace FanOMax.Core.Analysis;

/// <summary>Paramètres de détection des échelons de puissance.</summary>
public sealed record StepDetectionOptions
{
    /// <summary>Hausse minimale de puissance pour considérer qu'une charge démarre (W).</summary>
    public double MinPowerJump { get; init; } = 30;

    /// <summary>Durée de la fenêtre de référence avant l'échelon (s).</summary>
    public double BaselineSeconds { get; init; } = 5;

    /// <summary>Fenêtre de la réaction « instantanée » de la puce après l'échelon (s).</summary>
    public double ImmediateSeconds { get; init; } = 3;

    /// <summary>Durée minimale de maintien de la charge pour analyser la réponse (s).</summary>
    public double MinHoldSeconds { get; init; } = 30;

    /// <summary>Durée maximale analysée après l'échelon (s).</summary>
    public double MaxHoldSeconds { get; init; } = 900;

    /// <summary>Fenêtre de moyenne utilisée pour estimer la température finale (s).</summary>
    public double FinalWindowSeconds { get; init; } = 10;
}

/// <summary>
/// Réponse de la température à un échelon de puissance.
/// Les délais <c>T50</c>, <c>T63</c> et <c>T90</c> sont mesurés depuis le début de l'échelon,
/// en proportion de la hausse totale (avant → finale).
/// </summary>
public sealed record StepResponse(
    double StartTime,
    double HoldSeconds,
    double PowerBefore,
    double PowerDuring,
    double TempBefore,
    double TempFinal,
    double ImmediateRise,
    double? T50,
    double? T63,
    double? T90)
{
    public double TotalRise => TempFinal - TempBefore;

    /// <summary>Part de la hausse totale obtenue dans les premières secondes (réaction de la puce).</summary>
    public double ImmediateShare => TotalRise > 0 ? ImmediateRise / TotalRise : 0;

    /// <summary>Hausse de température par watt supplémentaire (°C/W), à ventilation constante.</summary>
    public double DegreesPerWatt => PowerDuring - PowerBefore > 0 ? TotalRise / (PowerDuring - PowerBefore) : 0;
}

/// <summary>
/// Détecte les échelons de puissance (début d'une charge) et mesure comment la température y répond.
/// Sert à calibrer la prédiction et le simulateur thermique.
/// </summary>
public static class StepResponseAnalyzer
{
    public static IReadOnlyList<StepResponse> Analyze(
        IReadOnlyList<double> time,
        IReadOnlyList<double?> power,
        IReadOnlyList<double?> temperature,
        StepDetectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(power);
        ArgumentNullException.ThrowIfNull(temperature);
        if (power.Count != time.Count || temperature.Count != time.Count)
        {
            throw new ArgumentException("Les séries doivent avoir la même longueur.");
        }

        options ??= new StepDetectionOptions();
        var results = new List<StepResponse>();
        var n = time.Count;
        var i = 1;

        while (i < n)
        {
            var t0 = time[i];
            var powerBefore = Mean(time, power, t0 - options.BaselineSeconds, t0, includeEnd: false);
            var powerAfter = Mean(time, power, t0, t0 + options.ImmediateSeconds, includeEnd: true);

            if (powerBefore is null || powerAfter is null || powerAfter - powerBefore < options.MinPowerJump)
            {
                i++;
                continue;
            }

            // La charge est maintenue tant que la puissance (lissée sur 3 s) reste au-dessus de la moitié du saut.
            var threshold = powerBefore.Value + (0.5 * (powerAfter.Value - powerBefore.Value));
            var end = i;
            while (end + 1 < n
                   && time[end + 1] - t0 <= options.MaxHoldSeconds
                   && (Mean(time, power, time[end + 1] - 3, time[end + 1], includeEnd: true) ?? 0) >= threshold)
            {
                end++;
            }

            var hold = time[end] - t0;
            if (hold < options.MinHoldSeconds)
            {
                i++;
                continue;
            }

            var tempBefore = Mean(time, temperature, t0 - options.BaselineSeconds, t0, includeEnd: false);
            var tempImmediate = Mean(time, temperature, t0, t0 + options.ImmediateSeconds, includeEnd: true);
            var tempFinal = Mean(time, temperature, time[end] - options.FinalWindowSeconds, time[end], includeEnd: true);
            var powerDuring = Mean(time, power, t0, time[end], includeEnd: true);

            if (tempBefore is not null && tempImmediate is not null && tempFinal is not null && powerDuring is not null)
            {
                var rise = tempFinal.Value - tempBefore.Value;
                results.Add(new StepResponse(
                    StartTime: t0,
                    HoldSeconds: hold,
                    PowerBefore: powerBefore.Value,
                    PowerDuring: powerDuring.Value,
                    TempBefore: tempBefore.Value,
                    TempFinal: tempFinal.Value,
                    ImmediateRise: tempImmediate.Value - tempBefore.Value,
                    T50: TimeToReach(time, temperature, i, end, tempBefore.Value, rise, 0.5),
                    T63: TimeToReach(time, temperature, i, end, tempBefore.Value, rise, 0.632),
                    T90: TimeToReach(time, temperature, i, end, tempBefore.Value, rise, 0.9)));
            }

            i = end + 1;
        }

        return results;
    }

    /// <summary>Délai (depuis l'échelon) pour que la température lissée atteigne une fraction de la hausse totale.</summary>
    private static double? TimeToReach(
        IReadOnlyList<double> time,
        IReadOnlyList<double?> temperature,
        int start,
        int end,
        double tempBefore,
        double rise,
        double fraction)
    {
        // En dessous de 2 °C de hausse, le bruit du capteur rend la mesure de délai sans intérêt.
        if (rise < 2)
        {
            return null;
        }

        var target = tempBefore + (fraction * rise);
        for (var k = start; k <= end; k++)
        {
            var smoothed = Mean(time, temperature, time[k] - 3, time[k], includeEnd: true);
            if (smoothed >= target)
            {
                return time[k] - time[start];
            }
        }

        return null;
    }

    /// <summary>Moyenne des valeurs dont l'horodatage est dans [from, to) ou [from, to]. <paramref name="time"/> doit être croissant.</summary>
    private static double? Mean(IReadOnlyList<double> time, IReadOnlyList<double?> values, double from, double to, bool includeEnd)
    {
        double sum = 0;
        var count = 0;
        for (var k = LowerBound(time, from); k < time.Count; k++)
        {
            var t = time[k];
            if (t > to || (!includeEnd && t >= to))
            {
                break;
            }

            if (values[k] is { } v && double.IsFinite(v))
            {
                sum += v;
                count++;
            }
        }

        return count > 0 ? sum / count : null;
    }

    /// <summary>Premier index dont l'horodatage est ≥ <paramref name="value"/>.</summary>
    private static int LowerBound(IReadOnlyList<double> time, double value)
    {
        int lo = 0, hi = time.Count;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (time[mid] < value)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }
}
