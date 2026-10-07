namespace FanOMax.Core.Simulation;

/// <summary>
/// Paramètres du simulateur thermique à deux nœuds :
/// <list type="bullet">
/// <item><b>puce</b> : réagit en quelques secondes, <c>T_puce → T_ventirad + R_puce·P</c> ;</item>
/// <item><b>ventirad</b> : inertie thermique, <c>C·dT/dt = P − (T − T_ambiante) / R_ventirad(ventilo)</c>.</item>
/// </list>
/// </summary>
public sealed record ThermalSimulatorParameters
{
    /// <summary>Température ambiante (°C).</summary>
    public double Ambient { get; init; } = 24.4;

    /// <summary>Résistance thermique puce → ventirad (°C/W).</summary>
    public double DieResistance { get; init; } = 0.39;

    /// <summary>Constante de temps de la puce (s).</summary>
    public double DieTimeConstant { get; init; } = 2.5;

    /// <summary>Résistance ventirad → air à <see cref="LowFanPercent"/> de ventilation (°C/W).</summary>
    public double SinkResistanceAtLowFan { get; init; } = 0.15;

    /// <summary>Résistance ventirad → air à 100 % de ventilation (°C/W).</summary>
    public double SinkResistanceAtFullFan { get; init; } = 0.03;

    public double LowFanPercent { get; init; } = 30;

    /// <summary>Capacité thermique du ventirad (J/K).</summary>
    public double SinkHeatCapacity { get; init; } = 800;

    /// <summary>Constante de temps de réponse des ventilateurs (s).</summary>
    public double FanTimeConstant { get; init; } = 2;

    /// <summary>THM limit du CPU (°C), null si aucune.</summary>
    public double? ThermalLimit { get; init; } = 80;

    /// <summary>Le CPU se stabilise à <c>ThermalLimit − ThermalLimitMargin</c> en régime limité.</summary>
    public double ThermalLimitMargin { get; init; } = 1;

    /// <summary>Écart-type du bruit de mesure de la température (°C).</summary>
    public double MeasurementNoise { get; init; } = 0.4;

    /// <summary>
    /// Ryzen 7 5800X + ventirad, calé sur les captures de la phase 1 :
    /// repos à 100 % (31 W → 37,4 °C), Cinebench à 100 % (128 W → 78 °C), jeu à 49 % (88 W → 69,6 °C),
    /// réponse à un échelon (51 % de la hausse en 3 s, 90 % en 7 s).
    /// </summary>
    public static ThermalSimulatorParameters Ryzen5800XAirCooler { get; } = new();

    /// <summary>Résistance du ventirad selon la ventilation : interpolation linéaire, extrapolée sous le point bas.</summary>
    public double SinkResistance(double fanPercent)
    {
        var fan = Math.Clamp(fanPercent, 0, 100);
        var slope = (SinkResistanceAtFullFan - SinkResistanceAtLowFan) / (100 - LowFanPercent);
        return SinkResistanceAtLowFan + (slope * (fan - LowFanPercent));
    }
}

/// <summary>État du simulateur après un pas.</summary>
/// <param name="MeasuredTemperature">Température vue par le capteur (avec bruit).</param>
/// <param name="DieTemperature">Température réelle de la puce.</param>
/// <param name="SinkTemperature">Température du ventirad.</param>
/// <param name="EffectivePower">Puissance réellement consommée (réduite par la limite thermique).</param>
/// <param name="FanPercent">Ventilation réelle (avec l'inertie des ventilateurs).</param>
public readonly record struct SimulatorState(
    double MeasuredTemperature,
    double DieTemperature,
    double SinkTemperature,
    double EffectivePower,
    double FanPercent)
{
    /// <summary>Vrai si le CPU a dû réduire sa puissance à cause de la limite thermique.</summary>
    public bool IsThermalLimited(double demandedPower) => EffectivePower < demandedPower - 0.5;
}

/// <summary>
/// Simulateur thermique CPU + ventirad, pour régler et tester la régulation sans toucher au matériel.
/// Déterministe pour une graine donnée.
/// </summary>
public sealed class ThermalSimulator
{
    private readonly ThermalSimulatorParameters _p;
    private readonly Random _random;
    private double _die;
    private double _sink;
    private double _fan;

    public ThermalSimulator(ThermalSimulatorParameters parameters, double initialPower = 30, double initialFan = 30, int seed = 42)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        _p = parameters;
#pragma warning disable CA5394 // Bruit de simulation : un générateur non cryptographique est voulu (reproductible).
        _random = new Random(seed);
#pragma warning restore CA5394
        _fan = initialFan;
        _sink = _p.Ambient + (_p.SinkResistance(initialFan) * initialPower);
        _die = _sink + (_p.DieResistance * initialPower);
    }

    /// <param name="dtSeconds">Pas de temps.</param>
    /// <param name="demandedPower">Puissance demandée par la charge (W).</param>
    /// <param name="fanCommand">Consigne de ventilation (%).</param>
    public SimulatorState Step(double dtSeconds, double demandedPower, double fanCommand)
    {
        _fan += (fanCommand - _fan) * (1 - Math.Exp(-dtSeconds / _p.FanTimeConstant));
        var sinkResistance = _p.SinkResistance(_fan);

        // Limite thermique : le CPU réduit son boost pour que la puce reste sous la limite.
        var power = demandedPower;
        if (_p.ThermalLimit is { } limit)
        {
            var allowed = (limit - _p.ThermalLimitMargin - _sink) / _p.DieResistance;
            power = Math.Clamp(allowed, 0, demandedPower);
        }

        // Mise à jour exacte de chaque nœud du 1er ordre (stable quel que soit le pas).
        var sinkTarget = _p.Ambient + (sinkResistance * power);
        var sinkTau = sinkResistance * _p.SinkHeatCapacity;
        _sink += (sinkTarget - _sink) * (1 - Math.Exp(-dtSeconds / sinkTau));

        var dieTarget = _sink + (_p.DieResistance * power);
        _die += (dieTarget - _die) * (1 - Math.Exp(-dtSeconds / _p.DieTimeConstant));

        return new SimulatorState(_die + (_p.MeasurementNoise * NextGaussian()), _die, _sink, power, _fan);
    }

    private double NextGaussian()
    {
#pragma warning disable CA5394
        var u1 = 1 - _random.NextDouble();
        var u2 = _random.NextDouble();
#pragma warning restore CA5394
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }
}
