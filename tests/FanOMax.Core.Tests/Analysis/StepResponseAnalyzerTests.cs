using FanOMax.Core.Analysis;

namespace FanOMax.Core.Tests.Analysis;

public class StepResponseAnalyzerTests
{
    /// <summary>
    /// Modèle thermique synthétique à 1 Hz : une part instantanée (la puce) et une part lente
    /// du 1er ordre (le ventirad), à ventilation constante.
    /// T = 40 + fast·P + lente, avec lente → slow·P selon la constante de temps tau.
    /// </summary>
    private static (double[] Time, double?[] Power, double?[] Temp) Simulate(
        Func<int, double> powerAt, int seconds, double fast = 0.1, double slow = 0.2, double tau = 40)
    {
        var time = new double[seconds];
        var power = new double?[seconds];
        var temp = new double?[seconds];
        var basePower = powerAt(0);
        var slowPart = slow * basePower;

        for (var t = 0; t < seconds; t++)
        {
            var p = powerAt(t);
            slowPart += (slow * p - slowPart) * (1 - Math.Exp(-1 / tau));
            time[t] = t;
            power[t] = p;
            temp[t] = 40 + fast * p + slowPart;
        }

        return (time, power, temp);
    }

    [Fact]
    public void Analyze_DetectsASustainedStep_AndMeasuresItsResponse()
    {
        // 60 s au repos (30 W), puis 300 s de charge (120 W), puis retour au repos.
        var (time, power, temp) = Simulate(t => t is >= 60 and < 360 ? 120 : 30, 420);

        var steps = StepResponseAnalyzer.Analyze(time, power, temp);

        var step = Assert.Single(steps);
        Assert.InRange(step.StartTime, 59, 61);
        Assert.InRange(step.PowerBefore, 29, 31);
        Assert.InRange(step.PowerDuring, 115, 121);

        // Part instantanée : 0,1 °C/W × 90 W = 9 °C, plus un peu de part lente sur les 3 premières secondes.
        Assert.InRange(step.ImmediateRise, 9, 12);

        // Hausse totale ≈ 9 + 0,2 × 90 = 27 °C (la part lente est quasi établie après 300 s = 7,5 τ).
        Assert.InRange(step.TotalRise, 25, 28);

        // 63 % de la hausse totale ≈ 17 °C, dont 9 °C instantanés : la part lente doit fournir 8/18 ≈ 45 %,
        // soit environ 0,6 τ ≈ 24 s (plus un léger retard dû au lissage sur 3 s).
        Assert.NotNull(step.T63);
        Assert.InRange(step.T63!.Value, 15, 35);
        Assert.True(step.T50 <= step.T63 && step.T63 <= step.T90);
    }

    [Fact]
    public void Analyze_IgnoresShortSpikes()
    {
        // Pic de 10 s : trop court pour être une charge soutenue (minimum 30 s).
        var (time, power, temp) = Simulate(t => t is >= 60 and < 70 ? 140 : 30, 200);

        Assert.Empty(StepResponseAnalyzer.Analyze(time, power, temp));
    }

    [Fact]
    public void Analyze_IgnoresSmallPowerChanges()
    {
        var (time, power, temp) = Simulate(t => t >= 60 ? 50 : 30, 300);

        Assert.Empty(StepResponseAnalyzer.Analyze(time, power, temp));
    }

    [Fact]
    public void Analyze_DetectsSeveralSeparateSteps()
    {
        // Deux charges séparées par une pause de 2 minutes.
        var (time, power, temp) = Simulate(t => t is (>= 60 and < 180) or (>= 300 and < 420) ? 120 : 30, 500);

        var steps = StepResponseAnalyzer.Analyze(time, power, temp);

        Assert.Equal(2, steps.Count);
        Assert.InRange(steps[1].StartTime, 299, 301);
    }

    [Fact]
    public void Analyze_ToleratesMissingValues()
    {
        var (time, power, temp) = Simulate(t => t is >= 60 and < 360 ? 120 : 30, 420);
        for (var i = 0; i < temp.Length; i += 7)
        {
            temp[i] = null;
        }

        var step = Assert.Single(StepResponseAnalyzer.Analyze(time, power, temp));
        Assert.InRange(step.TotalRise, 24, 29);
    }

    [Fact]
    public void Analyze_RejectsSeriesOfDifferentLengths()
    {
        Assert.Throws<ArgumentException>(() => StepResponseAnalyzer.Analyze([0, 1], [1.0], [1.0, 2.0]));
    }
}
