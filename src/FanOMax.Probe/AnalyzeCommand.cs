using System.Globalization;
using System.Text;
using FanOMax.Core.Analysis;
using FanOMax.Core.Sensors;

namespace FanOMax.Probe;

/// <summary>
/// Analyse un CSV produit par <c>record</c> : statistiques, temps passé au-dessus de la cible,
/// et réponse de la température aux échelons de puissance (calibrage de la prédiction).
/// Ne nécessite pas les droits administrateur.
/// </summary>
internal static class AnalyzeCommand
{
    public static int Run(CommandLine cli)
    {
        if (cli.Arguments.Count == 0 || !File.Exists(cli.Arguments[0]))
        {
            Console.Error.WriteLine("Usage : analyze <fichier.csv> [--target 70] [--temp <texte>] [--power <texte>]");
            return 1;
        }

        var path = cli.Arguments[0];
        var data = CaptureFile.Load(path);
        var target = cli.GetDouble("target", 70);

        var report = BuildReport(path, data, cli, target);
        var reportPath = Path.ChangeExtension(path, ".analysis.md");
        File.WriteAllText(reportPath, report, new UTF8Encoding(false));

        Console.WriteLine(report);
        Console.WriteLine($"Rapport écrit : {Path.GetFullPath(reportPath)}");
        return 0;
    }

    private static string BuildReport(string path, CaptureFile data, CommandLine cli, double target)
    {
        var sb = new StringBuilder();
        var duration = data.Time.Length > 0 ? data.Time[^1] : 0;

        sb.AppendLine(CultureInfo.InvariantCulture, $"# Analyse : {Path.GetFileName(path)}");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Durée : {TimeSpan.FromSeconds(duration):hh\\:mm\\:ss} · {data.Time.Length} échantillons");
        sb.AppendLine();

        var cpuTemp = data.Resolve(cli.Get("temp"), KeySensor.CpuTemperature);
        var cpuPower = data.Resolve(cli.Get("power"), KeySensor.CpuPower);
        var gpuTemp = data.Resolve(null, KeySensor.GpuTemperature);
        var gpuPower = data.Resolve(null, KeySensor.GpuPower);
        var gpuHotSpot = data.Resolve(null, KeySensor.GpuHotSpot);
        var cpuLoad = data.Resolve(null, KeySensor.CpuLoad);
        var gpuLoad = data.Resolve(null, KeySensor.GpuLoad);
        var cpuClock = data.Resolve(null, KeySensor.CpuClock);

        AppendStats(sb, data, cpuTemp, cpuPower, cpuLoad, cpuClock, gpuTemp, gpuHotSpot, gpuPower, gpuLoad);
        AppendTarget(sb, data, cpuTemp, target);
        AppendSteps(sb, "CPU", data, cpuPower, cpuTemp, new StepDetectionOptions { MinPowerJump = cli.GetDouble("min-jump", 30) });
        AppendSteps(sb, "GPU", data, gpuPower, gpuTemp, new StepDetectionOptions { MinPowerJump = cli.GetDouble("gpu-min-jump", 50) });
        AppendFps(sb, data);

        return sb.ToString();
    }

    private static void AppendStats(StringBuilder sb, CaptureFile data, params int[] keyColumns)
    {
        sb.AppendLine("## Statistiques");
        sb.AppendLine();
        sb.AppendLine("| Capteur | Identifiant | Min | Moyenne | P95 | Max |");
        sb.AppendLine("|---|---|---|---|---|---|");

        // Capteurs clés, puis tous les ventilateurs et contrôles PWM.
        var columns = keyColumns.Where(c => c >= 0)
            .Concat(Enumerable.Range(0, data.Sensors.Count).Where(i => SensorDescriptor.KindFromId(data.Sensors[i].Id) is SensorKind.Fan or SensorKind.Control))
            .Distinct();

        foreach (var c in columns)
        {
            var (id, name) = data.Sensors[c];
            var unit = SensorDescriptor.KindFromId(id).Unit();
            var s = SeriesStats.Compute(data.Values[c]);
            sb.AppendLine(s is null
                ? $"| {name} | `{id}` | — | — | — | — |"
                : $"| {name} | `{id}` | {F(s.Min)} {unit} | {F(s.Mean)} {unit} | {F(s.P95)} {unit} | {F(s.Max)} {unit} |");
        }

        sb.AppendLine();
    }

    private static void AppendTarget(StringBuilder sb, CaptureFile data, int cpuTemp, double target)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"## Cible CPU {F(target, "0")} °C");
        sb.AppendLine();
        if (cpuTemp < 0)
        {
            sb.AppendLine("⚠️ Température CPU introuvable (utiliser --temp).");
            sb.AppendLine();
            return;
        }

        var temps = data.Values[cpuTemp].Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        if (temps.Length == 0)
        {
            sb.AppendLine("Aucune valeur.");
            sb.AppendLine();
            return;
        }

        var above = temps.Count(t => t > target);

        // Moyenne glissante sur 10 s : ignore les pics brefs du Ryzen, représente ce que verra le PID.
        double sum = 0, smoothedMax = double.MinValue;
        for (var i = 0; i < temps.Length; i++)
        {
            sum += temps[i] - (i >= 10 ? temps[i - 10] : 0);
            smoothedMax = Math.Max(smoothedMax, sum / Math.Min(10, i + 1));
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"- Maximum brut : **{F(temps.Max())} °C** · maximum lissé (10 s) : **{F(smoothedMax)} °C**");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Temps au-dessus de {F(target, "0")} °C : **{F(100.0 * above / temps.Length)} %** ({above} échantillons)");
        sb.AppendLine(smoothedMax <= target
            ? "- ✅ La cible est tenue sur cette capture."
            : "- ❌ La cible est dépassée. Si les ventilateurs étaient à 100 % pendant la charge, elle est hors d'atteinte pour ce refroidissement (voir PLAN.md §5).");
        sb.AppendLine();
    }

    private static void AppendSteps(StringBuilder sb, string label, CaptureFile data, int power, int temp, StepDetectionOptions options)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"## Réponse thermique {label} (échelons de puissance ≥ {F(options.MinPowerJump, "0")} W)");
        sb.AppendLine();
        if (power < 0 || temp < 0)
        {
            sb.AppendLine("⚠️ Capteur de puissance ou de température introuvable.");
            sb.AppendLine();
            return;
        }

        var steps = StepResponseAnalyzer.Analyze(data.Time, data.Values[power], data.Values[temp], options);
        if (steps.Count == 0)
        {
            sb.AppendLine("Aucun échelon détecté (il faut une charge soutenue d'au moins 30 s après une période calme).");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Début | Durée | Puissance avant → pendant | Temp. avant → finale | Hausse immédiate (3 s) | T50 | T63 | T90 | °C/W |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var s in steps)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {TimeSpan.FromSeconds(s.StartTime):hh\\:mm\\:ss} | {F(s.HoldSeconds, "0")} s | {F(s.PowerBefore, "0")} → {F(s.PowerDuring, "0")} W "
                          + $"| {F(s.TempBefore)} → {F(s.TempFinal)} °C | +{F(s.ImmediateRise)} °C ({F(100 * s.ImmediateShare, "0")} %) "
                          + $"| {Sec(s.T50)} | {Sec(s.T63)} | {Sec(s.T90)} | {F(s.DegreesPerWatt, "0.###")} |");
        }

        sb.AppendLine();
        sb.AppendLine("> **Lecture :** la hausse immédiate est la réaction de la puce (pas de délai). T50/T63/T90 = délai pour atteindre 50/63/90 % de la hausse totale : "
                      + "c'est l'avance dont dispose la prédiction sur la puissance. Les ventilateurs varient pendant la capture, donc les °C/W sont indicatifs.");
        sb.AppendLine();
    }

    private static void AppendFps(StringBuilder sb, CaptureFile data)
    {
        var fps = SeriesStats.Compute(data.Fps);
        if (fps is null)
        {
            return;
        }

        var apps = data.FpsApp.Where(a => a.Length > 0)
            .GroupBy(a => a, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key} ({g.Count()} s)");

        sb.AppendLine("## FPS");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Applications : {string.Join(", ", apps)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Min {F(fps.Min, "0")} · moyenne {F(fps.Mean, "0")} · max {F(fps.Max, "0")} ({fps.Count} échantillons avec FPS)");
        sb.AppendLine();
    }

    private static string F(double value, string format = "0.0") => value.ToString(format, CultureInfo.InvariantCulture);

    private static string Sec(double? seconds) => seconds is { } s ? $"{F(s, "0")} s" : "—";
}
