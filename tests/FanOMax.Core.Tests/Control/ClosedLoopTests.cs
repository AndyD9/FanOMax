using FanOMax.Core.Control;
using FanOMax.Core.Simulation;
using Xunit.Abstractions;

namespace FanOMax.Core.Tests.Control;

/// <summary>
/// Régulateur + simulateur thermique en boucle fermée, sur les vraies séries de puissance de la phase 1.
/// </summary>
public class ClosedLoopTests(ITestOutputHelper output)
{
    private const int Settle = 180;

    private static readonly CaptureData Game = CaptureData.Load(CaptureData.Game);

    [Theory]
    [InlineData(FanProfile.Silence)]
    [InlineData(FanProfile.Normal)]
    [InlineData(FanProfile.Perf)]
    public void Game_HoldsTheTarget_WithoutOscillation(FanProfile profile)
    {
        var settings = RegulatorSettings.Cpu(profile);

        var result = ClosedLoop.Run(settings, ClosedLoop.Trace(Game.CpuPower), Game.Length);
        output.WriteLine($"{profile} (cible {settings.TargetTemperature} °C) : {result.Summary(Settle)}");

        var target = settings.TargetTemperature;
        Assert.InRange(result.MeanTemperature(Settle), target - 2, target + 1);
        Assert.True(result.MaxTemperature10s(Settle) <= target + 3);
        Assert.True(result.FanTravelPerMinute(Settle) < 20, "les ventilateurs font le yoyo");
    }

    [Fact]
    public void Game_StaysStable_WhenTheFansAreLessEffectiveThanModelled()
    {
        // L'effet réel des ventilateurs est incertain (0,085 à 0,153 °C/% selon le calibrage) :
        // on simule un ventirad sur lequel la ventilation agit deux fois moins que dans le modèle.
        var weakFans = ThermalSimulatorParameters.Ryzen5800XAirCooler with { SinkResistanceAtLowFan = 0.09, Ambient = 27 };
        var settings = RegulatorSettings.Cpu(FanProfile.Normal);

        var result = ClosedLoop.Run(settings, ClosedLoop.Trace(Game.CpuPower), Game.Length, weakFans);
        output.WriteLine($"Ventilateurs peu efficaces : {result.Summary(Settle)}");

        Assert.InRange(result.MeanTemperature(Settle), settings.TargetTemperature - 2, settings.TargetTemperature + 1.5);
        Assert.True(result.FanTravelPerMinute(Settle) < 20, "les ventilateurs font le yoyo");
    }

    [Fact]
    public void Game_StaysStable_WhenTheFansAreMoreEffectiveThanModelled()
    {
        var strongFans = ThermalSimulatorParameters.Ryzen5800XAirCooler with { SinkResistanceAtLowFan = 0.22, Ambient = 21 };
        var settings = RegulatorSettings.Cpu(FanProfile.Normal);

        var result = ClosedLoop.Run(settings, ClosedLoop.Trace(Game.CpuPower), Game.Length, strongFans);
        output.WriteLine($"Ventilateurs très efficaces : {result.Summary(Settle)}");

        Assert.InRange(result.MeanTemperature(Settle), settings.TargetTemperature - 2, settings.TargetTemperature + 1.5);
        Assert.True(result.FanTravelPerMinute(Settle) < 20, "les ventilateurs font le yoyo");
    }

    [Fact]
    public void Idle_StaysNearTheMinimum()
    {
        var idle = CaptureData.Load(CaptureData.Idle);
        var settings = RegulatorSettings.Cpu(FanProfile.Normal);

        var result = ClosedLoop.Run(settings, ClosedLoop.Trace(idle.CpuPower), idle.Length);
        output.WriteLine($"Repos : {result.Summary(60)}");

        Assert.True(result.MeanFan(60) < settings.MinPercent + 10);
        Assert.True(result.MaxTemperature10s(60) < settings.TargetTemperature);
    }

    [Fact]
    public void HeavyLoad_EntersThermalLimitedMode_ThenRecoversWithoutWindup()
    {
        var cinebench = CaptureData.Load(CaptureData.Cinebench);
        var settings = RegulatorSettings.Cpu(FanProfile.Normal);

        var result = ClosedLoop.Run(settings, ClosedLoop.Trace(cinebench.CpuPower), cinebench.Length);

        // Charge Cinebench : de ≈ 15 s à ≈ 735 s dans la capture.
        var duringLoad = result.Steps.Where(s => s.Time is >= 120 and <= 700).ToList();
        var limitedShare = duringLoad.Count(s => s.Decision.Mode == RegulatorMode.ThermalLimited) / (double)duringLoad.Count;
        output.WriteLine($"Charge lourde : {limitedShare:P0} du temps en régime limité, {result.Summary(120)}");

        Assert.True(limitedShare > 0.9);
        Assert.All(duringLoad.Where(s => s.Decision.Mode == RegulatorMode.ThermalLimited),
            s => Assert.InRange(s.Decision.Percent, 74, 76));

        // Après la charge : retour vers le minimum sans intégrale emballée.
        var end = result.Steps[^1];
        output.WriteLine($"Fin : {end.Decision.Percent:0} % ({end.Decision.Mode}), correction {end.Decision.Correction:0.0}");
        Assert.True(end.Decision.Percent < 50);
        Assert.InRange(end.Decision.Correction, -10, 10);
    }

    [Fact]
    public void HeavyLoad_PerfKeepsMorePowerThanSilence()
    {
        static double Power(int t) => t < 30 ? 35 : 128;

        var silence = ClosedLoop.Run(RegulatorSettings.Cpu(FanProfile.Silence), Power, 900);
        var perf = ClosedLoop.Run(RegulatorSettings.Cpu(FanProfile.Perf), Power, 900);
        output.WriteLine($"Silence : {silence.Summary(120)}");
        output.WriteLine($"Perf    : {perf.Summary(120)}");

        // Perf ventile plus et laisse le CPU consommer (donc booster) davantage sous la limite thermique.
        Assert.True(perf.MeanFan(120) > silence.MeanFan(120) + 30);
        Assert.True(perf.MeanEffectivePower(120) > silence.MeanEffectivePower(120) + 5);
        Assert.True(silence.MaxTemperature10s(120) <= 80);
    }

    [Fact]
    public void LoadStep_FansFollowQuickly_WithoutLongOvershoot()
    {
        // 2 min au repos (30 W), puis une charge de type jeu (95 W).
        static double Power(int t) => t < 120 ? 30 : 95;
        var settings = RegulatorSettings.Cpu(FanProfile.Normal);

        var result = ClosedLoop.Run(settings, Power, 900);
        var final = result.MeanFan(600);
        var reach = result.Steps.First(s => s.Time >= 120 && s.Decision.Percent >= 0.9 * final).Time - 120;
        var overshootSeconds = result.Steps.Count(s => s.Time >= 120 && s.State.DieTemperature > settings.TargetTemperature + 3);
        output.WriteLine($"Échelon 30 → 95 W : 90 % de la ventilation finale ({final:0} %) en {reach} s, {overshootSeconds} s au-dessus de cible + 3 °C");

        Assert.True(reach <= 40);
        Assert.True(overshootSeconds <= 30);
    }

    [Fact]
    public void ShortSensorGlitch_DoesNotDisturbTheFans()
    {
        var settings = RegulatorSettings.Cpu(FanProfile.Normal);

        var result = ClosedLoop.Run(settings, _ => 88, 600, temperatureOverride: (t, measured) => t is 400 or 401 ? double.NaN : measured);

        var before = result.Steps[399].Decision.Percent;
        var during = result.Steps.Where(s => s.Time is >= 400 and <= 405).ToList();
        Assert.Contains(during, s => s.Decision.Mode == RegulatorMode.SensorHolding);
        Assert.All(during, s => Assert.InRange(s.Decision.Percent, before - 2, before + 2));
    }

    [Fact]
    public void LostTemperature_GoesToFailsafeImmediately()
    {
        var settings = RegulatorSettings.Cpu(FanProfile.Silence);

        var result = ClosedLoop.Run(settings, _ => 60, 600, temperatureOverride: (t, measured) => t >= 400 ? null : measured);

        var lost = result.Steps.First(s => s.Decision.Mode == RegulatorMode.SensorLost);
        Assert.InRange(lost.Time, 401, 405);
        Assert.Equal(settings.FailsafePercent, lost.Decision.Percent);
        Assert.NotNull(lost.Decision.Reason);
    }
}
