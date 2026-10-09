using System.Globalization;
using FanOMax.Core.Control;

namespace FanOMax.Core.Tests.Control;

/// <summary>
/// Plancher de protection, sur le vrai pilotage du 2026-10-09 (09:41:41 → 09:56:23, F1 Manager 24, cible 69 °C) :
/// à 09:55:30, une charge concentrée a porté le Tctl de 69 à 84,9 °C en 25 s à puissance constante (≈ 80 W),
/// alors que le PI seul ne montait la ventilation que de 38 à 52 %.
/// </summary>
public class ProtectionFloorTests
{
    private const int SpikeStart = 829;   // 09:55:30 dans la série
    private const int SpikePeak = 857;    // 09:55:58 : 84,9 °C

    private static (double[] Temp, double[] Power) Trace()
    {
        var rows = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Data", "pilotage-pic-20261009.csv"))
            .Skip(1)
            .Select(l => l.Split(','))
            .ToArray();
        return (
            rows.Select(r => double.Parse(r[1], CultureInfo.InvariantCulture)).ToArray(),
            rows.Select(r => double.Parse(r[2], CultureInfo.InvariantCulture)).ToArray());
    }

    private static List<RegulatorDecision> Replay(RegulatorSettings settings)
    {
        var (temp, power) = Trace();
        var regulator = new FanRegulator(settings);
        regulator.Prime(50);
        return temp.Select((t, i) => regulator.Update(1, t, power[i])).ToList();
    }

    private static RegulatorSettings Cpu69 => RegulatorSettings.Cpu(FanProfile.Normal) with { TargetTemperature = 69 };

    [Fact]
    public void Spike_ProtectionFloor_RaisesTheFansMuchFaster()
    {
        var withFloor = Replay(Cpu69);
        var withoutFloor = Replay(Cpu69 with { ProtectionCurve = null });

        // Sans plancher (comportement du 2026-10-09) : ≈ 50 % au sommet du pic.
        Assert.InRange(withoutFloor[SpikePeak].Percent, 40, 60);

        // Avec plancher : ventilation forte au sommet du pic (montée limitée à 5 %/s).
        Assert.True(withFloor[SpikePeak].Percent >= 85, $"{withFloor[SpikePeak].Percent:0} %");
        Assert.Contains(withFloor.Skip(SpikeStart), d => d.Mode == RegulatorMode.Protection);
    }

    [Fact]
    public void NormalGaming_ProtectionFloorIsRareAndDiscreet()
    {
        // 14 minutes de jeu à 67–72 °C avant le pic. Mesuré : le plancher n'intervient que 2 s (montée réelle à 75 °C),
        // pour quelques % de ventilation en plus. Il ne doit pas changer le comportement en jeu normal.
        var withFloor = Replay(Cpu69).Take(SpikeStart).ToList();
        var withoutFloor = Replay(Cpu69 with { ProtectionCurve = null }).Take(SpikeStart).ToList();

        Assert.True(withFloor.Count(d => d.Mode == RegulatorMode.Protection) <= 5);
        var maxExtra = withFloor.Zip(withoutFloor, (a, b) => a.Percent - b.Percent).Max();
        Assert.True(maxExtra <= 6, $"{maxExtra:0.0} % de plus");
        Assert.Equal(withoutFloor.Average(d => d.Percent), withFloor.Average(d => d.Percent), precision: 0);
    }

    [Fact]
    public void OneSecondSpike_BarelyMovesTheFans()
    {
        var regulator = new FanRegulator(Cpu69);
        regulator.Prime(40);
        for (var i = 0; i < 60; i++)
        {
            regulator.Update(1, 68, 84);
        }

        var before = regulator.Update(1, 68, 84).Percent;
        var spike = regulator.Update(1, 80, 84).Percent;
        var after = regulator.Update(1, 68, 84).Percent;

        // Lissage 2 s : un pic isolé de 80 °C ne déclenche pas le plancher (74 °C).
        Assert.True(spike - before <= 3, $"{before:0.0} → {spike:0.0}");
        Assert.True(after - before <= 3);
    }

    [Fact]
    public void ThermalLimited_TheProfileKeepsControl()
    {
        // Charge lourde soutenue (type Cinebench) : en régime limité, Silence garde ses 50 % malgré le plancher.
        var regulator = new FanRegulator(RegulatorSettings.Cpu(FanProfile.Silence));
        RegulatorDecision decision = default;
        for (var i = 0; i < 300; i++)
        {
            // Légère fluctuation, comme un vrai Tctl (une valeur strictement constante serait un capteur figé).
            decision = regulator.Update(1, 79 + ((i % 3) * 0.125), 128);
        }

        Assert.Equal(RegulatorMode.ThermalLimited, decision.Mode);
        Assert.Equal(50, decision.Percent, precision: 0);
    }
}
