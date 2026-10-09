using FanOMax.Core.Control;
using FanOMax.Core.Sensors;
using FanOMax.Hardware;
using FanOMax.Service.Configuration;

namespace FanOMax.Service.Engine;

/// <summary>Groupe de ventilateurs résolu sur le matériel réel : index des capteurs, sorties PWM, régulateur.</summary>
internal sealed class GroupRuntime
{
    private GroupRuntime(GroupOptions options, RegulatorSettings settings, int temperatureIndex, int powerIndex, string[] controlIds, int[] controlSensorIndices)
    {
        Options = options;
        Settings = settings;
        Regulator = new FanRegulator(settings);
        TemperatureIndex = temperatureIndex;
        PowerIndex = powerIndex;
        ControlIds = controlIds;
        ControlSensorIndices = controlSensorIndices;
    }

    public GroupOptions Options { get; }

    public string Name => Options.Name;

    public RegulatorSettings Settings { get; }

    public FanRegulator Regulator { get; private set; }

    public int TemperatureIndex { get; }

    public int PowerIndex { get; }

    public IReadOnlyList<string> ControlIds { get; }

    /// <summary>Index des capteurs « Control » correspondants, pour lire le % réellement appliqué.</summary>
    public IReadOnlyList<int> ControlSensorIndices { get; }

    /// <summary>Vrai quand le groupe a été rendu au BIOS après une perte de capteur.</summary>
    public bool HandedBack { get; set; }

    /// <summary>Durée de valeurs valides depuis la perte de capteur (reprise de la main).</summary>
    public double RecoveredFor { get; set; }

    /// <summary>Dernier statut journalisé, pour ne tracer que les changements.</summary>
    public string? LastStatus { get; set; }

    /// <summary>Dernière consigne du régulateur, pour estimer la température en mode fantôme.</summary>
    public double? LastDecisionPercent { get; set; }

    /// <summary>
    /// Écart de température estimé entre la ventilation de FanOMax et celle réellement appliquée, filtré sur 30 s
    /// (inertie du ventirad). Utilisé uniquement quand FanOMax ne pilote pas.
    /// </summary>
    public Ema ShadowOffset { get; } = new(30);

    /// <summary>
    /// Repart d'un régulateur neuf (filtres, intégrale, moyenne de puissance), par exemple au réveil du PC :
    /// l'état accumulé avant la veille ne décrit plus la situation.
    /// </summary>
    public void ResetRegulator()
    {
        Regulator = new FanRegulator(Settings);
        ShadowOffset.Reset();
        LastDecisionPercent = null;
    }

    /// <summary>Résout le groupe sur le matériel. Renvoie null et une erreur si un capteur ou une sortie est introuvable.</summary>
    public static GroupRuntime? Resolve(GroupOptions options, FanProfile profile, IHardwareBackend backend, out string? error)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(backend);

        var sensors = backend.Sensors.Select(s => (s.Id, s.Name)).ToList();
        var temperatureKey = options.Kind == GroupKind.Cpu ? KeySensor.CpuTemperature : KeySensor.GpuHotSpot;
        var powerKey = options.Kind == GroupKind.Cpu ? KeySensor.CpuPower : KeySensor.GpuPower;

        var temperature = FindSensor(sensors, options.TemperatureSensor, temperatureKey);
        var power = FindSensor(sensors, options.PowerSensor, powerKey);
        if (temperature < 0)
        {
            error = $"Groupe {options.Name} : capteur de température introuvable ({Describe(options.TemperatureSensor)}).";
            return null;
        }

        if (power < 0)
        {
            error = $"Groupe {options.Name} : capteur de puissance introuvable ({Describe(options.PowerSensor)}).";
            return null;
        }

        var known = backend.GetControls().Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var missing = options.Controls.Where(c => !known.Contains(c)).ToList();
        if (missing.Count > 0)
        {
            error = $"Groupe {options.Name} : sortie(s) PWM introuvable(s) : {string.Join(", ", missing)}.";
            return null;
        }

        var controlIds = options.Controls.Distinct(StringComparer.Ordinal).ToArray();
        var controlSensors = controlIds.Select(id => sensors.FindIndex(s => s.Id == id)).ToArray();

        var settings = options.Kind == GroupKind.Cpu ? RegulatorSettings.Cpu(profile) : RegulatorSettings.Gpu(profile);
        if (options.TargetTemperature is { } target)
        {
            settings = settings with { TargetTemperature = target };
        }

        error = null;
        return new GroupRuntime(options, settings, temperature, power, controlIds, controlSensors);
    }

    private static int FindSensor(List<(string Id, string Name)> sensors, string? id, KeySensor fallback) =>
        string.IsNullOrWhiteSpace(id) ? KeySensorResolver.Find(sensors, fallback) : sensors.FindIndex(s => s.Id == id);

    private static string Describe(string? id) => string.IsNullOrWhiteSpace(id) ? "détection automatique" : id;
}
