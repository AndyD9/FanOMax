using FanOMax.Core.Calibration;
using FanOMax.Core.Control;
using Xunit.Abstractions;

namespace FanOMax.Core.Tests.Calibration;

public class ThermalModelCalibratorTests(ITestOutputHelper output)
{
    private static List<CalibrationSample> Synthetic(StaticThermalModel truth, Func<int, double> fanAt, int count = 60)
    {
        var samples = new List<CalibrationSample>();
        for (var i = 0; i < count; i++)
        {
            var power = 30 + (i * 97 % 100);
            var fan = fanAt(i);
            var noise = ((i * 37 % 11) - 5) * 0.1;
            samples.Add(new CalibrationSample(truth.PredictTemperature(power, fan) + noise, power, fan));
        }

        return samples;
    }

    [Fact]
    public void Fit_RecoversTheCoefficients_WhenTheFanVaries()
    {
        var truth = new StaticThermalModel(35, 0.45, 0.15);

        var result = ThermalModelCalibrator.Fit(Synthetic(truth, i => 30 + (i * 53 % 70)));

        Assert.NotNull(result);
        Assert.True(result.FanEffectIdentified);
        Assert.Equal(0.45, result.Model.PowerGain, precision: 2);
        Assert.Equal(0.15, result.Model.FanGain, precision: 2);
        Assert.True(result.RmsError < 0.5);
    }

    [Fact]
    public void Fit_UsesTheFallbackFanGain_WhenTheFanBarelyVaries()
    {
        var truth = new StaticThermalModel(35, 0.45, 0.15);

        var result = ThermalModelCalibrator.Fit(Synthetic(truth, _ => 50), fallback: truth);

        Assert.NotNull(result);
        Assert.False(result.FanEffectIdentified);
        Assert.Equal(0.15, result.Model.FanGain);
        Assert.Equal(0.45, result.Model.PowerGain, precision: 2);
    }

    [Fact]
    public void Fit_ExcludesPointsNearTheThermalLimit()
    {
        var samples = Synthetic(new StaticThermalModel(35, 0.45, 0.15), i => 30 + (i * 53 % 70));
        var nearLimit = samples.Count(s => s.Temperature >= 77);

        var result = ThermalModelCalibrator.Fit(samples, thermalLimit: 80, limitMargin: 3);

        Assert.NotNull(result);
        Assert.Equal(nearLimit, result.ExcludedNearLimit);
        Assert.Equal(samples.Count - nearLimit, result.SampleCount);
    }

    [Fact]
    public void Fit_ReturnsNull_WithTooFewSamples()
    {
        Assert.Null(ThermalModelCalibrator.Fit([new CalibrationSample(50, 40, 30)]));
    }

    [Fact]
    public void Windows_KeepsOnlyStablePowerWindows()
    {
        // 30 s stables, puis 30 s de puissance qui oscille fortement.
        var time = Enumerable.Range(0, 60).Select(i => (double)i).ToArray();
        var power = time.Select(t => (double?)(t < 30 ? 50 : (t % 2 == 0 ? 30 : 120))).ToArray();
        var temp = time.Select(_ => (double?)60).ToArray();
        var fan = time.Select(_ => (double?)40).ToArray();

        var windows = ThermalModelCalibrator.Windows(time, temp, power, fan);

        Assert.Equal(3, windows.Count);
        Assert.All(windows, w => Assert.Equal(50, w.Power));
    }

    [Fact]
    public void Fit_OnRealCaptures_GivesAPlausibleModel()
    {
        var samples = new[] { CaptureData.Game, CaptureData.Idle, CaptureData.Cinebench }
            .Select(CaptureData.Load)
            .SelectMany(c => ThermalModelCalibrator.Windows(c.Time, c.CpuTemperature, c.CpuPower, c.FanPercent))
            .ToList();

        var withLimit = ThermalModelCalibrator.Fit(samples, thermalLimit: 80, fallback: StaticThermalModel.Ryzen5800XPhase1);
        var withoutLimit = ThermalModelCalibrator.Fit(samples);

        Assert.NotNull(withLimit);
        Assert.NotNull(withoutLimit);
        output.WriteLine($"Sans exclusion : {withoutLimit}");
        output.WriteLine($"THM 80 exclue  : {withLimit}");

        // Ordres de grandeur physiques : 0,3 à 0,6 °C/W, ventilateur qui refroidit.
        Assert.InRange(withLimit.Model.PowerGain, 0.3, 0.6);
        Assert.True(withLimit.Model.FanGain > 0);
        Assert.True(withLimit.RmsError < 3);
    }

    [Fact]
    public void Fit_OnRealGpuData_GivesAPlausibleHotSpotModel()
    {
        // Seule la capture de jeu charge le GPU ; sa ventilation y varie de 30 à 80 %.
        var game = CaptureData.Load(CaptureData.Game);
        var samples = ThermalModelCalibrator.Windows(game.Time, game.GpuHotSpot, game.GpuPower, game.GpuFanPercent, maxPowerStdDev: 10);

        var result = ThermalModelCalibrator.Fit(samples, fallback: StaticThermalModel.RadeonRx6750XtPhase1);

        Assert.NotNull(result);
        output.WriteLine($"GPU (point chaud) : {result}");
        Assert.InRange(result.Model.PowerGain, 0.05, 0.5);
        Assert.True(result.RmsError < 3);

        // Le modèle figé dans le code doit rester cohérent avec ce calibrage.
        var frozen = StaticThermalModel.RadeonRx6750XtPhase1;
        Assert.Equal(frozen.PowerGain, result.Model.PowerGain, precision: 2);
        Assert.Equal(frozen.FanGain, result.Model.FanGain, precision: 2);
    }

    [Fact]
    public void GpuModel_PredictsTheMeasuredGameAverage()
    {
        // Mesuré en jeu : 126 W, ventilateur à 58 % → point chaud moyen 60,9 °C.
        Assert.InRange(StaticThermalModel.RadeonRx6750XtPhase1.PredictTemperature(126, 58), 59, 63);
    }
}
