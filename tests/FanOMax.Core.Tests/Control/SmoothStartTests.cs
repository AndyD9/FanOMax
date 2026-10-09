using FanOMax.Core.Control;

namespace FanOMax.Core.Tests.Control;

/// <summary>Démarrage en douceur quand FanOMax prend la main sur des ventilateurs qui tournent déjà.</summary>
public class SmoothStartTests
{
    [Fact]
    public void Prime_HoldsTheCurrentLevelDuringWarmUp_ThenRegulates()
    {
        var regulator = new FanRegulator(RegulatorSettings.Cpu(FanProfile.Normal));
        regulator.Prime(50);

        // Premier échantillon de puissance en rafale (130 W), puis charge légère (60 W) à 66 °C.
        var decisions = Enumerable.Range(0, 90).Select(t => regulator.Update(1, 66, t == 0 ? 130 : 60)).ToList();

        // Préchauffage (25 s) : la ventilation reste celle trouvée, la rafale initiale est ignorée.
        Assert.All(decisions.Take(24), d => Assert.Equal(50, d.Percent));
        Assert.All(decisions.Take(24), d => Assert.Equal(RegulatorMode.Normal, d.Mode));

        // Ensuite, régulation normale : charge légère, la ventilation redescend doucement (−1 %/s max).
        Assert.True(decisions[^1].Percent < 50);
        Assert.True(decisions.Zip(decisions.Skip(1), (a, b) => a.Percent - b.Percent).Max() <= 1.0001);
    }

    [Fact]
    public void Prime_DoesNotHold_WhenItIsAlreadyTooHot()
    {
        var regulator = new FanRegulator(RegulatorSettings.Cpu(FanProfile.Normal));
        regulator.Prime(30);

        // 75 °C pour une cible de 67 °C : au-delà de cible + 3 °C, on régule tout de suite.
        var decisions = Enumerable.Range(0, 5).Select(_ => regulator.Update(1, 75, 90)).ToList();

        Assert.True(decisions[^1].Percent > 40);
    }

    [Fact]
    public void TargetUnreachable_IsNotReportedDuringWarmUp()
    {
        var settings = RegulatorSettings.Cpu(FanProfile.Normal) with { ThermalLimit = null };
        var regulator = new FanRegulator(settings);

        var decisions = Enumerable.Range(0, 40).Select(_ => regulator.Update(1, 75, 140)).ToList();

        Assert.All(decisions.Take(24), d => Assert.NotEqual(RegulatorMode.TargetUnreachable, d.Mode));
        Assert.Equal(RegulatorMode.TargetUnreachable, decisions[^1].Mode);
    }
}
