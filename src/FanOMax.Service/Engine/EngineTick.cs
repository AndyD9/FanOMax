using FanOMax.Core.Control;
using FanOMax.Service.Configuration;

namespace FanOMax.Service.Engine;

/// <summary>Résultat d'un cycle pour un groupe.</summary>
/// <param name="Temperature">Température mesurée.</param>
/// <param name="RegulatorTemperature">
/// Température transmise au régulateur : la mesure quand FanOMax pilote ; en mode fantôme, la température estimée
/// avec la ventilation de FanOMax (voir <c>RegulationEngine.EstimateTemperature</c>).
/// </param>
/// <param name="Status">Mode du régulateur, ou « Critical » (température critique) ou « Bios » (rendu au BIOS).</param>
/// <param name="Percent">Consigne retenue (après la règle de température critique).</param>
/// <param name="AppliedPercent">
/// % réellement appliqué aux sorties PWM sans facteur (lu sur le matériel : FanControl, BIOS ou FanOMax), comparable à <paramref name="Percent"/>.
/// </param>
/// <param name="Written">Vrai si FanOMax a écrit la consigne sur le matériel pendant ce cycle.</param>
public sealed record GroupTick(
    string Name,
    double Target,
    double? Temperature,
    double? RegulatorTemperature,
    double? Power,
    RegulatorDecision Decision,
    double Percent,
    string Status,
    double? AppliedPercent,
    bool Written);

/// <summary>Résultat d'un cycle du moteur.</summary>
/// <param name="WritingAllowed">Vrai si le moteur pilote réellement les ventilateurs (mode Active et FanControl absent).</param>
public sealed record EngineTick(
    DateTime Timestamp,
    OperatingMode Mode,
    bool WritingAllowed,
    bool FanControlRunning,
    IReadOnlyList<GroupTick> Groups);
