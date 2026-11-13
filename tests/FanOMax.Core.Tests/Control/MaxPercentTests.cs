using FanOMax.Core.Control;

namespace FanOMax.Core.Tests.Control;

/// <summary>
/// Plafond de régulation (<see cref="RegulatorSettings.MaxPercent"/>), ajouté le 2026-11-13 : en jeu à 73 °C, l'anticipation
/// (2,9 % par watt) et le plancher de protection montaient par à-coups à 85–100 % pour gagner quelques degrés.
/// </summary>
public class MaxPercentTests
{
    private static RegulatorSettings Capped(FanProfile profile = FanProfile.Silence) =>
        RegulatorSettings.Cpu(profile) with { TargetTemperature = 73, MaxPercent = 70 };

    /// <summary>Légère fluctuation, comme un vrai Tctl (une valeur strictement constante serait un capteur figé).</summary>
    private static RegulatorDecision Hold(FanRegulator regulator, double temperature, double power, int seconds)
    {
        RegulatorDecision decision = default;
        for (var i = 0; i < seconds; i++)
        {
            decision = regulator.Update(1, temperature + ((i % 3) * 0.125), power);
        }

        return decision;
    }

    [Fact]
    public void HighPower_StopsAtTheCeiling_AndSaysTheTargetIsUnreachable()
    {
        // 118 W : le modèle demande 100 % pour tenir 73 °C.
        var decision = Hold(new FanRegulator(Capped()), 75.5, 118, 120);

        Assert.Equal(70, decision.Percent, precision: 6);
        Assert.Equal(RegulatorMode.TargetUnreachable, decision.Mode);
    }

    [Fact]
    public void ProtectionFloor_IsCappedToo()
    {
        var regulator = new FanRegulator(Capped());
        Hold(regulator, 70, 80, 60);

        // Charge concentrée : 83 °C à puissance modérée, le plancher du profil demanderait 100 %.
        var max = Enumerable.Range(0, 8).Max(i => regulator.Update(1, 83 + ((i % 3) * 0.125), 85).Percent);
        var decision = regulator.Update(1, 83, 85);

        Assert.Equal(RegulatorMode.Protection, decision.Mode);
        Assert.InRange(max, 50, 70);
    }

    [Fact]
    public void ThermalLimited_IsCappedToo()
    {
        // Perf ventile à 100 % en régime limité : le plafond l'emporte.
        var decision = Hold(new FanRegulator(Capped(FanProfile.Perf)), 79, 128, 300);

        Assert.Equal(RegulatorMode.ThermalLimited, decision.Mode);
        Assert.Equal(70, decision.Percent, precision: 6);
    }

    [Fact]
    public void WideDeadband_StillReachesTheMinimumAndTheCeiling()
    {
        var regulator = new FanRegulator(Capped() with { Deadband = 3 });

        Assert.Equal(70, Hold(regulator, 75.5, 118, 120).Percent, precision: 6);

        // Retour au repos : la descente va jusqu'à 25 %, pas 28 %.
        Assert.Equal(25, Hold(regulator, 50, 40, 300).Percent, precision: 6);
    }

    [Fact]
    public void WithoutCeiling_NothingChanges()
    {
        var decision = Hold(new FanRegulator(RegulatorSettings.Cpu(FanProfile.Silence) with { TargetTemperature = 73 }), 75.5, 118, 120);

        Assert.Equal(100, decision.Percent, precision: 6);
    }
}
