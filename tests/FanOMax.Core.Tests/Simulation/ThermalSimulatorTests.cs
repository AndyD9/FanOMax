using FanOMax.Core.Analysis;
using FanOMax.Core.Simulation;
using Xunit.Abstractions;

namespace FanOMax.Core.Tests.Simulation;

public class ThermalSimulatorTests(ITestOutputHelper output)
{
    private static readonly ThermalSimulatorParameters NoNoise = ThermalSimulatorParameters.Ryzen5800XAirCooler with { MeasurementNoise = 0 };

    private static SimulatorState RunSteady(double power, double fan, int seconds = 1200)
    {
        var sim = new ThermalSimulator(NoNoise, power, fan);
        SimulatorState state = default;
        for (var i = 0; i < seconds; i++)
        {
            state = sim.Step(1, power, fan);
        }

        return state;
    }

    [Theory]
    [InlineData(31, 100, 37.4)]   // repos, ventilateurs à 100 % (fin de capture Cinebench)
    [InlineData(88, 49, 69.6)]    // jeu (F1 Manager 24)
    [InlineData(128, 100, 78.0)]  // Cinebench, ventilateurs à 100 %
    public void SteadyState_MatchesThePhase1Measurements(double power, double fan, double measured)
    {
        var state = RunSteady(power, fan);

        Assert.InRange(state.DieTemperature, measured - 1.5, measured + 1.5);
    }

    [Fact]
    public void ThermalLimit_CapsTheTemperatureAndReducesPower()
    {
        // Cinebench avec peu de ventilation : le CPU doit se brider sous la THM limit (80 °C).
        var state = RunSteady(128, 30);

        Assert.InRange(state.DieTemperature, 78, 80);
        Assert.True(state.IsThermalLimited(128));
        output.WriteLine($"Cinebench à 30 % : {state.EffectivePower:0} W au lieu de 128 W");
    }

    [Fact]
    public void StepResponse_IsFastLikeTheRealChip()
    {
        // Même échelon que la capture Cinebench : 49 → 127 W, ventilateurs à 100 %.
        var sim = new ThermalSimulator(NoNoise, 49, 100);
        var time = new List<double>();
        var power = new List<double?>();
        var temp = new List<double?>();
        for (var t = 0; t < 600; t++)
        {
            var p = t < 60 ? 49 : 127;
            var state = sim.Step(1, p, 100);
            time.Add(t);
            power.Add(p);
            temp.Add(state.MeasuredTemperature);
        }

        var step = Assert.Single(StepResponseAnalyzer.Analyze(time, power, temp));
        output.WriteLine($"Simulé : hausse immédiate {step.ImmediateRise:0.0} °C ({step.ImmediateShare:P0}), T50 {step.T50} s, T63 {step.T63} s, T90 {step.T90} s");

        // Mesuré en phase 1 : 51 % en 3 s, T50 = 3 s, T63 = 4 s, T90 = 7 s.
        Assert.InRange(step.ImmediateShare, 0.4, 0.75);
        Assert.InRange(step.T50!.Value, 1, 5);
        Assert.InRange(step.T90!.Value, 4, 20);
    }

    /// <remarks>
    /// Au repos, le simulateur est ≈ 3,4 °C trop froid (biais connu et accepté) : la charge y arrive par rafales
    /// de moins d'une seconde sur un ou deux cœurs, et Tctl capte ces pics locaux que la moyenne de puissance
    /// à 1 Hz ne montre pas. Sans conséquence : au repos, la ventilation est au minimum.
    /// </remarks>
    [Theory]
    [InlineData(CaptureData.Game, 1.5)]
    [InlineData(CaptureData.Cinebench, 3)]
    [InlineData(CaptureData.Idle, 5)]
    public void Replay_ReproducesTheRealCaptures(string capture, double maxRms)
    {
        // On rejoue la puissance et la ventilation réellement mesurées, et on compare la température simulée à la vraie.
        var data = CaptureData.Load(capture);
        var sim = new ThermalSimulator(NoNoise, data.CpuPower[0] ?? 40, data.FanPercent[0] ?? 40);
        var simulated = new double[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            simulated[i] = sim.Step(1, data.CpuPower[i] ?? 40, data.FanPercent[i] ?? 40).DieTemperature;
        }

        // Comparaison sur des moyennes de 10 s : le Tctl brut est très bruité.
        var errors = Enumerable.Range(0, data.Length / 10)
            .Select(w => Enumerable.Range(w * 10, 10))
            .Select(idx => idx.Average(i => simulated[i]) - idx.Average(i => data.CpuTemperature[i] ?? double.NaN))
            .Where(double.IsFinite)
            .ToArray();
        var rms = Math.Sqrt(errors.Average(e => e * e));
        var bias = errors.Average();
        output.WriteLine($"{capture} : RMS {rms:0.00} °C, biais {bias:+0.00;-0.00} °C");

        Assert.True(rms < maxRms, $"RMS {rms:0.00} °C");
    }
}
