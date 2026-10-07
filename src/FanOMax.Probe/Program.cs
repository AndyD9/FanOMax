using FanOMax.Hardware;
using FanOMax.Probe;

// Sonde en lecture seule : n'écrit jamais les PWM. Peut tourner en même temps que FanControl.
var cli = CommandLine.Parse(args);

try
{
    return cli.Command switch
    {
        "inventory" => RequireHardwareAccess() ?? InventoryCommand.Run(cli),
        "record" => RequireHardwareAccess() ?? await RecordCommand.RunAsync(cli).ConfigureAwait(false),
        "analyze" => AnalyzeCommand.Run(cli),
        "calibrate" => CalibrateCommand.Run(cli),
        _ => PrintUsage(),
    };
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"Erreur : {ex.Message}");
    return 1;
}

static int? RequireHardwareAccess()
{
    if (!SystemChecks.IsAdministrator())
    {
        Console.Error.WriteLine("Droits administrateur requis pour lire les capteurs : relance depuis un terminal « Exécuter en tant qu'administrateur ».");
        return 2;
    }

    if (!SystemChecks.IsPawnIoInstalled())
    {
        Console.Error.WriteLine($"Driver PawnIO introuvable ({SystemChecks.PawnIoLibraryPath}) : les capteurs de la carte mère seront absents. Voir TROUBLESHOOT.md §4.1.");
    }

    return null;
}

static int PrintUsage()
{
    Console.WriteLine("""
        FanOMax.Probe : sonde matérielle en lecture seule

        Commandes :
          inventory [--out docs\hardware-inventory.md]
              Liste capteurs et contrôles PWM (admin requis).

          record [--label nom] [--duration 10m] [--interval 1s] [--fps] [--presentmon chemin] [--out captures]
              Enregistre les capteurs dans un CSV (admin requis). Ctrl+C pour arrêter.
              --fps : ajoute les FPS via PresentMon (tools\PresentMon*.exe par défaut).

          analyze <fichier.csv> [--target 70] [--temp texte] [--power texte] [--min-jump 30]
              Statistiques, cible CPU, réponse thermique aux échelons de puissance.

          calibrate <capture.csv> [autres.csv…] [--thm 80] [--cpu-fan texte] [--gpu-fan texte] [--out docs\thermal-model.md]
              Recalibre les modèles thermiques CPU et GPU (T ≈ a + b·P − c·Ventilo%).
        """);
    return 1;
}
