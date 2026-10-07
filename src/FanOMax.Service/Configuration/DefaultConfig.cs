using System.Globalization;
using System.Text;
using System.Text.Json;
using FanOMax.Core.Sensors;
using FanOMax.Hardware;

namespace FanOMax.Service.Configuration;

/// <summary>
/// Génère <c>config.json</c> au premier démarrage, à partir du matériel détecté :
/// capteurs clés, et sorties PWM dont le ventilateur tourne. Le fichier est commenté pour être édité à la main.
/// </summary>
public static class DefaultConfig
{
    /// <summary>Crée le fichier s'il n'existe pas. Renvoie vrai s'il a été créé.</summary>
    public static bool WriteIfMissing(string path, IHardwareBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        if (File.Exists(path))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, Build(backend), new UTF8Encoding(false));
        return true;
    }

    public static string Build(IHardwareBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        var sensors = backend.Sensors.Select(s => (s.Id, s.Name)).ToList();
        var controls = backend.GetControls();

        string? Id(KeySensor key) => KeySensorResolver.Find(sensors, key) is var i and >= 0 ? sensors[i].Id : null;

        // Sorties PWM dont le ventilateur associé tourne au démarrage (les canaux vides sont ignorés).
        List<string> Active(string prefix) => controls
            .Where(c => c.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && c.PairedFanRpm > 0)
            .Select(c => c.Id)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("""
            {
              // Configuration de FanOMax, rechargée à chaud à chaque enregistrement du fichier.
              // Une configuration invalide est refusée (voir les journaux) et la précédente reste active.
              // Générée au premier démarrage à partir du matériel détecté.
              "FanOMax": {
                // Shadow : calcule et journalise les décisions sans jamais toucher aux ventilateurs.
                // Active : pilote les ventilateurs (refusé tant que FanControl tourne).
                "Mode": "Shadow",

                // Silence | Normal | Perf (cibles et ventilation en régime limité : docs/phase2-regulation.md §2)
                "Profile": "Normal",

                // Période de la boucle de régulation (secondes)
                "IntervalSeconds": 1,

                "Groups": [
            """);

        AppendGroup(sb, "CPU", "Cpu", Id(KeySensor.CpuTemperature), Id(KeySensor.CpuPower), Active("/lpc/"), 90, last: false);
        AppendGroup(sb, "GPU", "Gpu", Id(KeySensor.GpuHotSpot), Id(KeySensor.GpuPower), Active("/gpu-"), 105, last: true);

        sb.AppendLine("""
                ],

                "Safety": {
                  // Boucle bloquée depuis plus de N secondes : le watchdog rend la main au BIOS
                  "WatchdogSeconds": 5,
                  // Erreurs consécutives avant arrêt du service (le BIOS garde alors la main)
                  "MaxConsecutiveErrors": 5,
                  // Après une perte de capteur, secondes de valeurs valides avant de reprendre la main
                  "ResumeAfterSeconds": 30
                },

                // Journal des décisions, seconde par seconde (C:\ProgramData\FanOMax\shadow)
                "ShadowLog": {
                  "Enabled": true,
                  "RetentionDays": 7
                }
              }
            }
            """);
        return sb.ToString();
    }

    private static void AppendGroup(StringBuilder sb, string name, string kind, string? temperature, string? power, List<string> controls, int critical, bool last)
    {
        sb.AppendLine("      {");
        sb.AppendLine(CultureInfo.InvariantCulture, $"        \"Name\": {Json(name)},");
        sb.AppendLine(CultureInfo.InvariantCulture, $"        \"Kind\": {Json(kind)},");
        sb.AppendLine(CultureInfo.InvariantCulture, $"        \"Enabled\": {(controls.Count > 0 ? "true" : "false")},");
        sb.AppendLine("        // Identifiants LibreHardwareMonitor (docs/hardware-inventory.md) ; vide = détection automatique");
        sb.AppendLine(CultureInfo.InvariantCulture, $"        \"TemperatureSensor\": {Json(temperature ?? "")},");
        sb.AppendLine(CultureInfo.InvariantCulture, $"        \"PowerSensor\": {Json(power ?? "")},");
        sb.AppendLine("        // Sorties PWM pilotées : celles dont le ventilateur tournait au premier démarrage");
        sb.AppendLine(CultureInfo.InvariantCulture, $"        \"Controls\": [ {string.Join(", ", controls.Select(Json))} ],");
        sb.AppendLine("        // Cible en °C ; null = cible du profil");
        sb.AppendLine("        \"TargetTemperature\": null,");
        sb.AppendLine("        // Au-delà : 100 % quel que soit le mode");
        sb.AppendLine(CultureInfo.InvariantCulture, $"        \"CriticalTemperature\": {critical}");
        sb.AppendLine(last ? "      }" : "      },");
    }

    private static string Json(string value) => JsonSerializer.Serialize(value);
}
