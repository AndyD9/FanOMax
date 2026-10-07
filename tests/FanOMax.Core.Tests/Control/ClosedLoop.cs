using FanOMax.Core.Control;
using FanOMax.Core.Simulation;

namespace FanOMax.Core.Tests.Control;

/// <summary>Un pas de la boucle fermée régulateur + simulateur.</summary>
internal readonly record struct LoopStep(int Time, double DemandedPower, RegulatorDecision Decision, SimulatorState State);

/// <summary>Résultat d'une simulation en boucle fermée, avec les indicateurs de qualité de la régulation.</summary>
internal sealed class LoopResult(IReadOnlyList<LoopStep> steps)
{
    public IReadOnlyList<LoopStep> Steps { get; } = steps;

    public IEnumerable<LoopStep> After(int seconds) => Steps.Where(s => s.Time >= seconds);

    public double MeanTemperature(int from = 0) => After(from).Average(s => s.State.DieTemperature);

    public double MeanFan(int from = 0) => After(from).Average(s => s.Decision.Percent);

    /// <summary>Écart-type de la consigne de ventilation : mesure de l'agitation des ventilateurs.</summary>
    public double FanStdDev(int from = 0)
    {
        var values = After(from).Select(s => s.Decision.Percent).ToArray();
        var mean = values.Average();
        return Math.Sqrt(values.Average(v => (v - mean) * (v - mean)));
    }

    /// <summary>Variation totale de la consigne par minute (%/min) : mesure du « yoyo » audible.</summary>
    public double FanTravelPerMinute(int from = 0)
    {
        var values = After(from).Select(s => s.Decision.Percent).ToArray();
        var travel = values.Zip(values.Skip(1), (a, b) => Math.Abs(b - a)).Sum();
        return travel / (values.Length / 60.0);
    }

    /// <summary>Température maximale sur des moyennes glissantes de 10 s.</summary>
    public double MaxTemperature10s(int from = 0)
    {
        var values = After(from).Select(s => s.State.DieTemperature).ToArray();
        return Enumerable.Range(0, Math.Max(1, values.Length - 9)).Max(i => values.Skip(i).Take(10).Average());
    }

    public double MeanEffectivePower(int from = 0) => After(from).Average(s => s.State.EffectivePower);

    public string Summary(int from = 0) =>
        $"T moy {MeanTemperature(from):0.0} °C, T max(10 s) {MaxTemperature10s(from):0.0} °C, ventilo moy {MeanFan(from):0} % (σ {FanStdDev(from):0.0}), "
        + $"course {FanTravelPerMinute(from):0} %/min, puissance moy {MeanEffectivePower(from):0} W";
}

internal static class ClosedLoop
{
    /// <param name="powerAt">Puissance demandée par la charge à chaque seconde.</param>
    /// <param name="temperatureOverride">Permet d'injecter un défaut capteur (renvoie la valeur à transmettre au régulateur).</param>
    public static LoopResult Run(
        RegulatorSettings settings,
        Func<int, double> powerAt,
        int seconds,
        ThermalSimulatorParameters? simulator = null,
        Func<int, double, double?>? temperatureOverride = null,
        int seed = 1)
    {
        var regulator = new FanRegulator(settings);
        var sim = new ThermalSimulator(simulator ?? ThermalSimulatorParameters.Ryzen5800XAirCooler, powerAt(0), settings.MinPercent, seed);
        var steps = new List<LoopStep>(seconds);
        var fan = settings.MinPercent;
        var lastPower = powerAt(0);
        var measured = sim.Step(1, lastPower, fan).MeasuredTemperature;

        for (var t = 0; t < seconds; t++)
        {
            var temperature = temperatureOverride is null ? measured : temperatureOverride(t, measured);

            // Le capteur de puissance mesure la puissance réellement consommée (bridée par la limite thermique).
            var decision = regulator.Update(1, temperature, lastPower);
            fan = decision.Percent;

            var demanded = powerAt(t);
            var state = sim.Step(1, demanded, fan);
            measured = state.MeasuredTemperature;
            lastPower = state.EffectivePower;
            steps.Add(new LoopStep(t, demanded, decision, state));
        }

        return new LoopResult(steps);
    }

    public static Func<int, double> Trace(double?[] power) =>
        t => power[Math.Min(t, power.Length - 1)] ?? 40;
}
