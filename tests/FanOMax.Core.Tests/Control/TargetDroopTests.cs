using FanOMax.Core.Control;
using Xunit.Abstractions;

namespace FanOMax.Core.Tests.Control;

/// <summary>
/// Cible glissante (<see cref="RegulatorSettings.TargetDroop"/>), ajoutée le 2026-11-13 : à cible fixe, l'anticipation
/// vaut 2,9 % de ventilation par watt, et le jeu (90–115 W) balaie toute la plage de ventilation.
/// </summary>
public class TargetDroopTests(ITestOutputHelper output)
{
    private static RegulatorSettings Fixed => RegulatorSettings.Cpu(FanProfile.Silence) with { TargetTemperature = 73, MaxPercent = 70 };

    private static RegulatorSettings Sliding => Fixed with { TargetDroop = 3 };

    /// <summary>Température cohérente avec le modèle pour une ventilation donnée, avec une légère fluctuation (capteur vivant).</summary>
    private static RegulatorDecision Settle_(FanRegulator regulator, double power, int seconds)
    {
        RegulatorDecision decision = default;
        var fan = regulator.Settings.MinPercent;
        for (var i = 0; i < seconds; i++)
        {
            var temperature = regulator.Settings.Model.PredictTemperature(power, fan) + ((i % 3) * 0.05);
            decision = regulator.Update(1, temperature, power + ((i % 2) * 0.1));
            fan = decision.Percent;
        }

        return decision;
    }

    [Fact]
    public void Feedforward_SlopeIsGentler()
    {
        // Entre 95 et 105 W, la cible fixe réclame 29 points de ventilation de plus ; la cible glissante de 3 °C, 30 % de moins.
        double Step(RegulatorSettings settings) =>
            Settle_(new FanRegulator(settings), 105, 600).Percent - Settle_(new FanRegulator(settings), 95, 600).Percent;

        var fixedStep = Step(Fixed);
        var slidingStep = Step(Sliding);
        output.WriteLine($"95 → 105 W : cible fixe +{fixedStep:0.0} pts, cible glissante +{slidingStep:0.0} pts");

        Assert.InRange(fixedStep, 25, 33);
        Assert.InRange(slidingStep, 17, 23);
    }

    [Fact]
    public void SteadyState_SitsOnTheSlidingTarget()
    {
        var regulator = new FanRegulator(Sliding);
        var decision = Settle_(regulator, 105, 900);

        // À l'équilibre, la température est sur la cible glissante : 73 °C + 3 °C × (ventilation − 25) / 45.
        var expected = 73 + (3 * (decision.Percent - 25) / 45);
        Assert.Equal(expected, regulator.EffectiveTarget, precision: 1);
        Assert.InRange(decision.FilteredTemperature!.Value, expected - 0.5, expected + 0.5);
        Assert.InRange(decision.Percent, 26, 69);
        Assert.Equal(RegulatorMode.Normal, decision.Mode);
    }

    [Fact]
    public void LowLoad_KeepsTheBaseTarget()
    {
        var regulator = new FanRegulator(Sliding);
        var decision = Settle_(regulator, 60, 300);

        Assert.Equal(25, decision.Percent, precision: 6);
        Assert.Equal(73, regulator.EffectiveTarget, precision: 6);
    }
}
