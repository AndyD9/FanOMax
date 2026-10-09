using FanOMax.Core.Control;

namespace FanOMax.Service.Configuration;

public enum OperatingMode
{
    /// <summary>Calcule et journalise les décisions sans jamais toucher aux ventilateurs.</summary>
    Shadow,

    /// <summary>Pilote les ventilateurs (refusé tant que FanControl tourne).</summary>
    Active,
}

public enum GroupKind
{
    Cpu,
    Gpu,
}

/// <summary>Configuration du service (section « FanOMax » de <c>config.json</c>), rechargée à chaud.</summary>
public sealed class FanOMaxOptions
{
    public const string Section = "FanOMax";

    public OperatingMode Mode { get; set; } = OperatingMode.Shadow;

    public FanProfile Profile { get; set; } = FanProfile.Normal;

    /// <summary>Période de la boucle de régulation (s).</summary>
    public double IntervalSeconds { get; set; } = 1;

    public List<GroupOptions> Groups { get; set; } = [];

    public SafetyOptions Safety { get; set; } = new();

    public ShadowLogOptions ShadowLog { get; set; } = new();
}

/// <summary>Groupe de ventilateurs régulé par une température et une puissance.</summary>
public sealed class GroupOptions
{
    public string Name { get; set; } = "";

    public GroupKind Kind { get; set; } = GroupKind.Cpu;

    public bool Enabled { get; set; } = true;

    /// <summary>Identifiant LHM du capteur de température ; vide = capteur clé détecté automatiquement.</summary>
    public string? TemperatureSensor { get; set; }

    /// <summary>Identifiant LHM du capteur de puissance ; vide = capteur clé détecté automatiquement.</summary>
    public string? PowerSensor { get; set; }

    /// <summary>Sorties PWM pilotées par ce groupe.</summary>
    public List<string> Controls { get; set; } = [];

    /// <summary>Cible (°C) ; null = cible du profil.</summary>
    public double? TargetTemperature { get; set; }

    /// <summary>Au-delà, 100 % quel que soit le mode ; null = 90 °C (CPU) ou 105 °C (GPU).</summary>
    public double? CriticalTemperature { get; set; }

    /// <summary>
    /// Ventilation fixe (%) au lieu de la régulation, pour identifier les ventilateurs ou tester (null = régulation).
    /// La règle de température critique reste prioritaire.
    /// </summary>
    public double? FixedPercent { get; set; }

    /// <summary>
    /// Facteur appliqué à la consigne du groupe, par sortie PWM (sortie absente = 1). Exemple : 0,6 sur l'avant = les
    /// ventilateurs d'admission reçoivent 60 % de la consigne, pour régler l'équilibre admission / extraction.
    /// Résultat borné entre 20 et 100 % ; ignoré à la température critique et en ventilation fixe (tout le groupe au même %).
    /// </summary>
    public Dictionary<string, double> ControlScales { get; set; } = new(StringComparer.Ordinal);

    public double EffectiveCriticalTemperature => CriticalTemperature ?? (Kind == GroupKind.Cpu ? 90 : 105);
}

public sealed class SafetyOptions
{
    /// <summary>Boucle bloquée depuis plus de N secondes : le watchdog rend la main au BIOS.</summary>
    public double WatchdogSeconds { get; set; } = 5;

    /// <summary>Erreurs consécutives avant arrêt du service (le BIOS garde alors la main).</summary>
    public int MaxConsecutiveErrors { get; set; } = 5;

    /// <summary>Après une perte de capteur, délai de valeurs valides avant de reprendre la main au BIOS (s).</summary>
    public double ResumeAfterSeconds { get; set; } = 30;
}

public sealed class ShadowLogOptions
{
    public bool Enabled { get; set; } = true;

    public int RetentionDays { get; set; } = 7;
}
