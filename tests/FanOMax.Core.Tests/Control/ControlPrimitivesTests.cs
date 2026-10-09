using FanOMax.Core.Control;

namespace FanOMax.Core.Tests.Control;

public class ControlPrimitivesTests
{
    [Fact]
    public void Ema_ReachesAbout63PercentAfterOneTimeConstant()
    {
        var ema = new Ema(10);
        ema.Update(0, 1);
        double value = 0;
        for (var i = 0; i < 10; i++)
        {
            value = ema.Update(100, 1);
        }

        Assert.InRange(value, 62, 64);
    }

    [Fact]
    public void TimeWindowAverage_ForgetsSamplesOlderThanTheWindow()
    {
        var average = new TimeWindowAverage(5);
        for (var i = 0; i < 10; i++)
        {
            average.Update(i < 5 ? 0 : 100, 1);
        }

        Assert.Equal(100, average.Value);
    }

    [Fact]
    public void SensorGuard_HoldsLastGoodValue_ThenDeclaresTheSensorLost()
    {
        var guard = new SensorGuard(new SensorGuardSettings(0, 115, HoldSeconds: 3));
        guard.Update(60, 1);

        Assert.Equal(60, guard.Update(double.NaN, 1));
        Assert.Equal(SensorStatus.Holding, guard.Status);
        Assert.Equal(60, guard.Update(null, 1));
        Assert.Equal(60, guard.Update(500, 1));
        Assert.Null(guard.Update(null, 1));
        Assert.Equal(SensorStatus.Lost, guard.Status);

        Assert.Equal(61, guard.Update(61, 1));
        Assert.Equal(SensorStatus.Ok, guard.Status);
    }

    [Fact]
    public void SensorGuard_DetectsAFrozenValue()
    {
        var guard = new SensorGuard(new SensorGuardSettings(0, 115, HoldSeconds: 0, FrozenSeconds: 30));
        for (var i = 0; i < 29; i++)
        {
            guard.Update(55.5, 1);
        }

        Assert.Equal(SensorStatus.Ok, guard.Status);
        guard.Update(55.5, 1);
        guard.Update(55.5, 1);
        Assert.Equal(SensorStatus.Lost, guard.Status);
        Assert.Equal("valeur figée", guard.Reason);
    }

    [Fact]
    public void GpuRegulator_ToleratesLongIdleWithIntegerReadings()
    {
        // Mesuré au repos : point chaud à 49 °C et puissance à 31 W, identiques pendant plus de 10 min.
        var regulator = new FanRegulator(RegulatorSettings.Gpu(FanProfile.Normal));

        var decisions = Enumerable.Range(0, 1200).Select(_ => regulator.Update(1, 49, 31)).ToList();

        Assert.All(decisions, d => Assert.Equal(RegulatorMode.Normal, d.Mode));
    }

    [Fact]
    public void CpuRegulator_StillDetectsAFrozenTemperature()
    {
        // Le Tctl du 5800X fluctue en permanence (série identique la plus longue mesurée : 9 s).
        var regulator = new FanRegulator(RegulatorSettings.Cpu(FanProfile.Normal));

        var decisions = Enumerable.Range(0, 200).Select(i => regulator.Update(1, 60, 40 + (i % 3))).ToList();

        Assert.Equal(RegulatorMode.SensorLost, decisions[^1].Mode);
    }

    [Fact]
    public void FanCurve_InterpolatesAndClampsAtTheEnds()
    {
        var curve = new FanCurve([(40, 30), (60, 50), (80, 100)]);

        Assert.Equal(30, curve.Evaluate(20));
        Assert.Equal(40, curve.Evaluate(50));
        Assert.Equal(75, curve.Evaluate(70));
        Assert.Equal(100, curve.Evaluate(95));
    }

    [Fact]
    public void FanCurve_Hysteresis_DelaysTheDescentOnly()
    {
        var curve = new FanCurve([(40, 30), (80, 70)], hysteresis: 3);

        Assert.Equal(60, curve.Update(70));
        Assert.Equal(60, curve.Update(68));   // −2 °C : sous l'hystérésis, la sortie ne bouge pas
        Assert.Equal(57, curve.Update(67));   // −3 °C : la courbe redescend
        Assert.Equal(62, curve.Update(72));   // une hausse est suivie immédiatement
    }

    [Fact]
    public void PiController_DoesNotWindUpWhileSaturated()
    {
        var pi = new PiController(new PiSettings(Kp: 2, Ki: 0.5, IntegralLimit: 30));

        // 5 min à 10 °C au-dessus de la consigne avec une sortie déjà saturée à 100 %.
        for (var i = 0; i < 300; i++)
        {
            pi.Update(80, 70, 1, feedforward: 100, 25, 100);
        }

        Assert.Equal(0, pi.Integral);
    }

    [Fact]
    public void PiController_IntegralIsBounded()
    {
        var pi = new PiController(new PiSettings(Kp: 0, Ki: 1, IntegralLimit: 20));
        for (var i = 0; i < 100; i++)
        {
            pi.Update(75, 70, 1, feedforward: 0, -100, 100);
        }

        Assert.Equal(20, pi.Integral);
    }

    [Fact]
    public void PiController_FreezeKeepsTheIntegral()
    {
        var pi = new PiController(new PiSettings(Kp: 0, Ki: 1));
        pi.Update(72, 70, 1, 50, 0, 100);
        var before = pi.Integral;

        pi.Update(90, 70, 1, 50, 0, 100, freeze: true);

        Assert.Equal(before, pi.Integral);
    }

    [Fact]
    public void OutputShaper_RisesFast_FallsSlowly_AndIgnoresTinyChanges()
    {
        var shaper = new OutputShaper(maxRisePerSecond: 5, maxFallPerSecond: 1, deadband: 1);
        shaper.Update(40, 1);

        Assert.Equal(45, shaper.Update(80, 1));
        Assert.Equal(44, shaper.Update(20, 1));
        Assert.Equal(44, shaper.Update(44.5, 1));
    }

    [Fact]
    public void StaticThermalModel_RequiredFanIsConsistentWithPrediction()
    {
        var model = StaticThermalModel.Ryzen5800XPhase1;

        var fan = model.RequiredFan(88, 67);

        Assert.Equal(67, model.PredictTemperature(88, fan), precision: 6);
        Assert.InRange(fan, 55, 65);
        Assert.Equal(88, model.MaxPowerFor(67, fan), precision: 6);
    }
}
