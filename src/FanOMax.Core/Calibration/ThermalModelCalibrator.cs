using FanOMax.Core.Control;

namespace FanOMax.Core.Calibration;

/// <summary>Point de calibrage : moyennes sur une fenêtre stable.</summary>
public sealed record CalibrationSample(double Temperature, double Power, double FanPercent);

/// <param name="Model">Modèle ajusté.</param>
/// <param name="RmsError">Erreur quadratique moyenne du modèle sur les points retenus (°C).</param>
/// <param name="SampleCount">Nombre de points retenus.</param>
/// <param name="ExcludedNearLimit">Points écartés car proches de la limite thermique.</param>
/// <param name="FanEffectIdentified">
/// Faux si la ventilation a trop peu varié dans les données (ou si l'effet ajusté est non physique) :
/// le coefficient du ventilateur est alors repris du modèle de repli.
/// </param>
/// <param name="FanSpread">Écart-type de la ventilation dans les points retenus (%).</param>
public sealed record CalibrationResult(
    StaticThermalModel Model,
    double RmsError,
    int SampleCount,
    int ExcludedNearLimit,
    bool FanEffectIdentified,
    double FanSpread);

/// <summary>
/// Ajuste le modèle statique <c>T = a + b·P − c·Ventilo</c> par moindres carrés sur des captures de la sonde.
/// </summary>
public static class ThermalModelCalibrator
{
    /// <summary>Écart-type minimal de ventilation pour que l'effet des ventilateurs soit identifiable (%).</summary>
    public const double MinFanSpread = 5;

    private const int MinSamples = 10;

    /// <summary>
    /// Découpe une capture en fenêtres et garde celles où la puissance est stable :
    /// la température y est proche de son équilibre, ce que décrit un modèle statique.
    /// </summary>
    public static List<CalibrationSample> Windows(
        IReadOnlyList<double> time,
        IReadOnlyList<double?> temperature,
        IReadOnlyList<double?> power,
        IReadOnlyList<double?> fan,
        double windowSeconds = 10,
        double maxPowerStdDev = 5)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(temperature);
        ArgumentNullException.ThrowIfNull(power);
        ArgumentNullException.ThrowIfNull(fan);

        var samples = new List<CalibrationSample>();
        var start = 0;
        while (start < time.Count)
        {
            var end = start;
            while (end < time.Count && time[end] - time[start] < windowSeconds)
            {
                end++;
            }

            var indices = Enumerable.Range(start, end - start)
                .Where(i => temperature[i] is { } t && double.IsFinite(t) && power[i] is { } p && double.IsFinite(p) && fan[i] is { } f && double.IsFinite(f))
                .ToList();

            // Fenêtre trop clairsemée (valeurs manquantes) : ignorée.
            if (indices.Count >= Math.Max(3, (end - start) * 0.8))
            {
                var powers = indices.Select(i => power[i]!.Value).ToList();
                var mean = powers.Average();
                var stdDev = Math.Sqrt(powers.Average(x => (x - mean) * (x - mean)));
                if (stdDev <= maxPowerStdDev)
                {
                    samples.Add(new CalibrationSample(
                        indices.Average(i => temperature[i]!.Value),
                        mean,
                        indices.Average(i => fan[i]!.Value)));
                }
            }

            start = end;
        }

        return samples;
    }

    /// <param name="thermalLimit">THM limit du CPU : les points à moins de <paramref name="limitMargin"/> °C sont écartés (température plafonnée par le CPU).</param>
    /// <param name="fallback">Modèle dont on reprend le coefficient de ventilation s'il n'est pas identifiable.</param>
    public static CalibrationResult? Fit(
        IReadOnlyList<CalibrationSample> samples,
        double? thermalLimit = null,
        double limitMargin = 3,
        StaticThermalModel? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var kept = thermalLimit is { } limit
            ? samples.Where(s => s.Temperature < limit - limitMargin).ToList()
            : samples.ToList();
        var excluded = samples.Count - kept.Count;
        if (kept.Count < MinSamples)
        {
            return null;
        }

        var fanMean = kept.Average(s => s.FanPercent);
        var fanSpread = Math.Sqrt(kept.Average(s => (s.FanPercent - fanMean) * (s.FanPercent - fanMean)));

        StaticThermalModel? model = null;
        if (fanSpread >= MinFanSpread)
        {
            var coefficients = LeastSquares.Solve(kept.Select(s => new[] { 1, s.Power, s.FanPercent }).ToArray(), kept.Select(s => s.Temperature).ToArray());

            // Un ventilateur qui réchaufferait le CPU n'a pas de sens physique : coefficient rejeté.
            if (coefficients is not null && coefficients[2] < 0)
            {
                model = new StaticThermalModel(coefficients[0], coefficients[1], -coefficients[2]);
            }
        }

        var identified = model is not null;
        if (model is null)
        {
            // Effet du ventilateur repris du modèle de repli, puis ajustement de a et b sur la température corrigée.
            var fanGain = fallback?.FanGain ?? 0;
            var coefficients = LeastSquares.Solve(
                kept.Select(s => new[] { 1, s.Power }).ToArray(),
                kept.Select(s => s.Temperature + (fanGain * s.FanPercent)).ToArray());
            if (coefficients is null)
            {
                return null;
            }

            model = new StaticThermalModel(coefficients[0], coefficients[1], fanGain);
        }

        var rms = Math.Sqrt(kept.Average(s => Math.Pow(s.Temperature - model.PredictTemperature(s.Power, s.FanPercent), 2)));
        return new CalibrationResult(model, rms, kept.Count, excluded, identified, fanSpread);
    }
}

/// <summary>Moindres carrés ordinaires par équations normales (petites dimensions).</summary>
public static class LeastSquares
{
    /// <summary>Renvoie les coefficients minimisant ‖X·β − y‖², ou null si le système est dégénéré.</summary>
    public static double[]? Solve(double[][] x, double[] y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        if (x.Length == 0 || x.Length != y.Length)
        {
            return null;
        }

        var n = x[0].Length;
        var a = new double[n, n + 1];
        for (var i = 0; i < x.Length; i++)
        {
            for (var r = 0; r < n; r++)
            {
                for (var c = 0; c < n; c++)
                {
                    a[r, c] += x[i][r] * x[i][c];
                }

                a[r, n] += x[i][r] * y[i];
            }
        }

        // Élimination de Gauss-Jordan avec pivot partiel.
        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < n; r++)
            {
                if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col]))
                {
                    pivot = r;
                }
            }

            if (Math.Abs(a[pivot, col]) < 1e-12)
            {
                return null;
            }

            for (var c = 0; c <= n; c++)
            {
                (a[col, c], a[pivot, c]) = (a[pivot, c], a[col, c]);
            }

            var divisor = a[col, col];
            for (var c = 0; c <= n; c++)
            {
                a[col, c] /= divisor;
            }

            for (var r = 0; r < n; r++)
            {
                if (r == col)
                {
                    continue;
                }

                var factor = a[r, col];
                for (var c = 0; c <= n; c++)
                {
                    a[r, c] -= factor * a[col, c];
                }
            }
        }

        return Enumerable.Range(0, n).Select(r => a[r, n]).ToArray();
    }
}
