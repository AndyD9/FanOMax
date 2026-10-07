using System.Globalization;
using System.Text;
using FanOMax.Core.Analysis;
using FanOMax.Core.Control;

namespace FanOMax.Probe;

/// <summary>
/// Bilan du mode fantôme : compare, groupe par groupe, les décisions de FanOMax à ce que FanControl
/// (ou le BIOS) a réellement appliqué, à partir des journaux <c>shadow-AAAAMMJJ.csv</c> du service.
/// </summary>
internal static class ShadowReportCommand
{
    private sealed record Row(DateTime Time, string Group, double? Target, double? Temperature, double? Decision, double? Applied, string Status, string Reason);

    public static int Run(CommandLine cli)
    {
        var directory = cli.Get("dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FanOMax", "shadow");
        var days = (int)cli.GetDouble("days", 1);
        if (!Directory.Exists(directory))
        {
            Console.Error.WriteLine($"Dossier introuvable : {directory} (le service est-il installé ?)");
            return 1;
        }

        var files = Directory.GetFiles(directory, "shadow-*.csv").Order(StringComparer.Ordinal).TakeLast(days).ToList();
        var rows = files.SelectMany(Read).ToList();
        if (rows.Count == 0)
        {
            Console.Error.WriteLine($"Aucune donnée dans {directory}.");
            return 1;
        }

        var report = Build(files, rows);
        Directory.CreateDirectory("captures");
        var output = cli.Get("out") ?? Path.Combine("captures", $"shadow-report-{DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.md");
        File.WriteAllText(output, report, new UTF8Encoding(false));
        Console.WriteLine(report);
        Console.WriteLine($"Rapport écrit : {Path.GetFullPath(output)}");
        return 0;
    }

    private static IEnumerable<Row> Read(string path)
    {
        // Le fichier du jour est ouvert en écriture par le service : lecture partagée.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var header = Csv.SplitLine(reader.ReadLine() ?? "");
        int Col(string name) => header.IndexOf(name);
        int time = Col("timestamp"), group = Col("group"), target = Col("target"), temp = Col("temperature"),
            decision = Col("decision_percent"), applied = Col("applied_percent"), status = Col("status"), reason = Col("reason");

        var rows = new List<Row>();
        while (reader.ReadLine() is { } line)
        {
            var f = Csv.SplitLine(line);
            if (f.Count < header.Count || !DateTime.TryParse(f[time], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            {
                continue;
            }

            rows.Add(new Row(t, f[group], Csv.ParseNumber(f[target]), Csv.ParseNumber(f[temp]), Csv.ParseNumber(f[decision]), Csv.ParseNumber(f[applied]), f[status], f[reason]));
        }

        return rows;
    }

    private static string Build(List<string> files, List<Row> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Bilan du mode fantôme");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Période : {rows.Min(r => r.Time):yyyy-MM-dd HH:mm} → {rows.Max(r => r.Time):yyyy-MM-dd HH:mm} · fichiers : {string.Join(", ", files.Select(Path.GetFileName))}");
        sb.AppendLine();
        sb.AppendLine("> « Appliqué » = ce que FanControl (ou le BIOS) imposait réellement. « FanOMax » = ce que FanOMax aurait appliqué.");
        sb.AppendLine("> La température « estimée avec FanOMax » corrige la température mesurée de l'écart de ventilation, via le modèle statique : c'est une **estimation**.");
        sb.AppendLine();

        foreach (var group in rows.GroupBy(r => r.Group, StringComparer.OrdinalIgnoreCase))
        {
            AppendGroup(sb, group.Key, group.OrderBy(r => r.Time).ToList());
        }

        return sb.ToString();
    }

    private static void AppendGroup(StringBuilder sb, string name, List<Row> rows)
    {
        var model = name.Contains("GPU", StringComparison.OrdinalIgnoreCase) ? StaticThermalModel.RadeonRx6750XtPhase1 : StaticThermalModel.Ryzen5800XPhase1;
        var target = rows.LastOrDefault(r => r.Target.HasValue)?.Target ?? double.NaN;
        var hours = (rows[^1].Time - rows[0].Time).TotalHours;

        sb.AppendLine(CultureInfo.InvariantCulture, $"## {name} (cible {F(target, "0")} °C)");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"{rows.Count} échantillons sur {F(hours, "0.0")} h.");
        sb.AppendLine();

        var measured = rows.Select(r => r.Temperature).ToList();
        var estimated = rows.Select(r => r.Temperature + (model.FanGain * (r.Applied - r.Decision))).ToList();

        sb.AppendLine("| | Ventilation moyenne | Ventilation P95 | Course (%/min) | Temp. moyenne | Temp. P95 | Temps > cible |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| **Appliqué (aujourd'hui)** | {Stat(rows.Select(r => r.Applied), s => s.Mean)} % | {Stat(rows.Select(r => r.Applied), s => s.P95)} % | {F(Travel(rows.Select(r => r.Applied)), "0")} | {Stat(measured, s => s.Mean)} °C | {Stat(measured, s => s.P95)} °C | {F(Above(measured, target), "0.0")} % |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| **FanOMax** (estimé) | {Stat(rows.Select(r => r.Decision), s => s.Mean)} % | {Stat(rows.Select(r => r.Decision), s => s.P95)} % | {F(Travel(rows.Select(r => r.Decision)), "0")} | {Stat(estimated, s => s.Mean)} °C | {Stat(estimated, s => s.P95)} °C | {F(Above(estimated, target), "0.0")} % |");
        sb.AppendLine();

        sb.AppendLine("Répartition des états du régulateur :");
        sb.AppendLine();
        foreach (var status in rows.GroupBy(r => r.Status).OrderByDescending(g => g.Count()))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"- {status.Key} : {F(100.0 * status.Count() / rows.Count, "0.0")} %");
        }

        var reasons = rows.Where(r => r.Reason.Length > 0).GroupBy(r => r.Reason).OrderByDescending(g => g.Count()).Take(5).ToList();
        if (reasons.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Défauts capteurs les plus fréquents :");
            sb.AppendLine();
            foreach (var reason in reasons)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- {reason.Key} : {reason.Count()} fois");
            }
        }

        sb.AppendLine();
    }

    private static string Stat(IEnumerable<double?> values, Func<SeriesStats, double> pick) =>
        SeriesStats.Compute(values) is { } s ? F(pick(s), "0.0") : "—";

    private static double Above(List<double?> values, double target)
    {
        var valid = values.Where(v => v.HasValue).ToList();
        return valid.Count == 0 ? 0 : 100.0 * valid.Count(v => v > target) / valid.Count;
    }

    /// <summary>Variation totale de la ventilation par minute (yoyo audible), en supposant un échantillon par seconde.</summary>
    private static double Travel(IEnumerable<double?> values)
    {
        var list = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (list.Count < 2)
        {
            return 0;
        }

        return list.Zip(list.Skip(1), (a, b) => Math.Abs(b - a)).Sum() / (list.Count / 60.0);
    }

    private static string F(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
}
