using System.Globalization;
using System.Text;
using FanOMax.Core.Sensors;
using FanOMax.Hardware;

namespace FanOMax.Probe;

/// <summary>Inventaire de tous les capteurs et contrôles, écrit en Markdown. Lecture seule.</summary>
internal static class InventoryCommand
{
    public static int Run(CommandLine cli)
    {
        var output = cli.Get("out") ?? Path.Combine("docs", "hardware-inventory.md");

        using var monitor = LhmMonitor.Open();

        // Deux lectures espacées : les charges (%) n'ont de sens qu'à partir de la deuxième.
        Thread.Sleep(1000);
        monitor.Update();

        var markdown = BuildMarkdown(monitor);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, markdown, new UTF8Encoding(false));

        // Rapport brut : utile au diagnostic, mais il peut contenir des numéros de série (captures/ n'est pas versionné).
        Directory.CreateDirectory("captures");
        var reportPath = Path.Combine("captures", "lhm-report.txt");
        File.WriteAllText(reportPath, monitor.GetReport(), new UTF8Encoding(false));

        PrintSummary(monitor);
        Console.WriteLine();
        Console.WriteLine($"Inventaire écrit : {Path.GetFullPath(output)}");
        Console.WriteLine($"Rapport brut LHM : {Path.GetFullPath(reportPath)} (ne pas publier)");
        return 0;
    }

    private static void PrintSummary(LhmMonitor monitor)
    {
        var tree = monitor.GetHardwareTree();
        Console.WriteLine("Matériel détecté :");
        foreach (var node in Flatten(tree))
        {
            Console.WriteLine($"  - {node.Name} ({node.Type}) : {node.Sensors.Count} capteurs");
        }

        var controls = monitor.GetControls();
        Console.WriteLine($"Contrôles de ventilateurs : {controls.Count}");
        foreach (var c in controls)
        {
            Console.WriteLine($"  - {c.HardwareName} / {c.Name} : {Fmt(c.CurrentPercent)} % ({c.Mode}), ventilateur associé : {Fmt(c.PairedFanRpm, "0")} RPM");
        }
    }

    private static string BuildMarkdown(LhmMonitor monitor)
    {
        var sb = new StringBuilder();
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        sb.AppendLine("# Inventaire matériel");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"> Généré par `FanOMax.Probe inventory` le {now}. Lecture seule : aucune écriture PWM.");
        sb.AppendLine();

        sb.AppendLine("## Environnement");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Droits administrateur : {YesNo(SystemChecks.IsAdministrator())}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Driver PawnIO : {YesNo(SystemChecks.IsPawnIoInstalled())}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- FanControl en cours d'exécution : {YesNo(SystemChecks.IsFanControlRunning())} (les % PWM ci-dessous sont alors ceux qu'il impose)");
        sb.AppendLine();

        AppendKeySensors(sb, monitor);
        AppendControls(sb, monitor.GetControls());

        sb.AppendLine("## Détail par matériel");
        sb.AppendLine();
        foreach (var node in monitor.GetHardwareTree())
        {
            AppendNode(sb, node, level: 3);
        }

        return sb.ToString();
    }

    private static void AppendKeySensors(StringBuilder sb, LhmMonitor monitor)
    {
        var ids = monitor.Sensors.Select(s => (s.Id, s.Name)).ToList();
        var values = monitor.ReadAll();

        sb.AppendLine("## Capteurs clés retenus");
        sb.AppendLine();
        sb.AppendLine("| Rôle | Capteur | Identifiant | Valeur |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var key in Enum.GetValues<KeySensor>())
        {
            var index = KeySensorResolver.Find(ids, key);
            if (index < 0)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"| {key} | ⚠️ introuvable | | |");
                continue;
            }

            var sensor = monitor.Sensors[index];
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {key} | {sensor.HardwareName} / {sensor.Name} | `{sensor.Id}` | {Fmt(values[index])} {sensor.Unit} |");
        }

        sb.AppendLine();
    }

    private static void AppendControls(StringBuilder sb, IReadOnlyList<ControlSnapshot> controls)
    {
        sb.AppendLine("## Contrôles de ventilateurs (PWM)");
        sb.AppendLine();
        if (controls.Count == 0)
        {
            sb.AppendLine("⚠️ Aucun contrôle détecté. Voir TROUBLESHOOT.md §4.1.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Contrôle | Matériel | Mode | % actuel | Plage | Ventilateur associé (probable) | RPM |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var c in controls)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {c.Name} `{c.Id}` | {c.HardwareName} | {c.Mode} | {Fmt(c.CurrentPercent)} | {Fmt(c.MinSoftwareValue, "0")}–{Fmt(c.MaxSoftwareValue, "0")} | {(c.PairedFanId is null ? "—" : $"`{c.PairedFanId}`")} | {Fmt(c.PairedFanRpm, "0")} |");
        }

        sb.AppendLine();
        sb.AppendLine("> L'association contrôle ↔ ventilateur est déduite de l'index du canal. Elle sera confirmée en phase 7 (variation d'un PWM et observation du RPM).");
        sb.AppendLine(">");
        sb.AppendLine("> Le mode « Undefined » est normal : il indique que **cette** instance de LHM (la sonde) ne pilote pas le canal. "
                      + "Cela ne dit rien de FanControl ou du BIOS, qui peuvent le piloter ; le « % actuel » est la consigne réellement appliquée.");
        sb.AppendLine();
    }

    private static void AppendNode(StringBuilder sb, HardwareNode node, int level)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"{new string('#', Math.Min(level, 6))} {node.Name} ({node.Type})");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Identifiant : `{node.Id}`");
        sb.AppendLine();

        if (node.Sensors.Count > 0)
        {
            sb.AppendLine("| Type | Nom | Identifiant | Valeur | Min | Max |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var s in node.Sensors.OrderBy(s => s.Descriptor.Kind).ThenBy(s => s.Descriptor.Id, StringComparer.Ordinal))
            {
                var unit = s.Descriptor.Unit;
                sb.AppendLine(CultureInfo.InvariantCulture, $"| {s.Descriptor.Kind} | {s.Descriptor.Name} | `{s.Descriptor.Id}` | {Fmt(s.Value)} {unit} | {Fmt(s.Min)} | {Fmt(s.Max)} |");
            }

            sb.AppendLine();
        }

        foreach (var child in node.Children)
        {
            AppendNode(sb, child, level + 1);
        }
    }

    private static IEnumerable<HardwareNode> Flatten(IEnumerable<HardwareNode> nodes) =>
        nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));

    private static string YesNo(bool value) => value ? "oui" : "non";

    private static string Fmt(float? value, string format = "0.#") =>
        value?.ToString(format, CultureInfo.InvariantCulture) ?? "—";
}
