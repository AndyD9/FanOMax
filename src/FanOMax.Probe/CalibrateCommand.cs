using System.Globalization;
using System.Text;
using FanOMax.Core.Calibration;
using FanOMax.Core.Control;
using FanOMax.Core.Sensors;

namespace FanOMax.Probe;

/// <summary>
/// Recalibre les modèles thermiques statiques (CPU et GPU) à partir d'une ou plusieurs captures de <c>record</c>.
/// À relancer après un changement matériel ou de réglages CPU (Hydra), ou après un test de ventilation dédié.
/// </summary>
internal static class CalibrateCommand
{
    public static int Run(CommandLine cli)
    {
        var files = cli.Arguments.Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            Console.Error.WriteLine("Usage : calibrate <capture.csv> [autres.csv…] [--thm 80] [--cpu-fan texte] [--gpu-fan texte] [--out docs\\thermal-model.md]");
            return 1;
        }

        var thm = cli.GetDouble("thm", 80);
        var output = cli.Get("out") ?? Path.Combine("docs", "thermal-model.md");

        var cpuSamples = new List<CalibrationSample>();
        var gpuSamples = new List<CalibrationSample>();
        var sources = new StringBuilder();

        foreach (var file in files)
        {
            var data = CaptureFile.Load(file);
            var cpuFan = string.IsNullOrWhiteSpace(cli.Get("cpu-fan")) ? data.FindActiveControl("/lpc/") : data.Find(cli.Get("cpu-fan")!);
            var gpuFan = string.IsNullOrWhiteSpace(cli.Get("gpu-fan")) ? data.FindActiveControl("/gpu-") : data.Find(cli.Get("gpu-fan")!);

            var cpu = Collect(data, data.Resolve(null, KeySensor.CpuTemperature), data.Resolve(null, KeySensor.CpuPower), cpuFan, maxPowerStdDev: 5);
            var gpu = Collect(data, data.Resolve(null, KeySensor.GpuHotSpot), data.Resolve(null, KeySensor.GpuPower), gpuFan, maxPowerStdDev: 10);
            cpuSamples.AddRange(cpu);
            gpuSamples.AddRange(gpu);

            sources.AppendLine(CultureInfo.InvariantCulture,
                $"| {Path.GetFileName(file)} | {data.Time.Length} | {cpu.Count} | {Name(data, cpuFan)} | {gpu.Count} | {Name(data, gpuFan)} |");
        }

        var cpuResult = ThermalModelCalibrator.Fit(cpuSamples, thermalLimit: thm, fallback: StaticThermalModel.Ryzen5800XPhase1);
        var gpuResult = ThermalModelCalibrator.Fit(gpuSamples, fallback: StaticThermalModel.RadeonRx6750XtPhase1);

        var report = new StringBuilder();
        report.AppendLine("# Modèle thermique calibré");
        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture,
            $"> Généré par `FanOMax.Probe calibrate` le {DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}. Modèle : `T ≈ a + b·P − c·Ventilo%`.");
        report.AppendLine();
        report.AppendLine("## Captures utilisées");
        report.AppendLine();
        report.AppendLine("| Fichier | Échantillons | Fenêtres CPU | Ventilation CPU | Fenêtres GPU | Ventilation GPU |");
        report.AppendLine("|---|---|---|---|---|---|");
        report.Append(sources);
        report.AppendLine();

        AppendResult(report, "CPU (Tctl)", cpuResult, StaticThermalModel.Ryzen5800XPhase1, $"Points à moins de 3 °C de la THM limit ({F(thm, "0")} °C) écartés : température plafonnée par le CPU.");
        AppendResult(report, "GPU (point chaud)", gpuResult, StaticThermalModel.RadeonRx6750XtPhase1, null);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, report.ToString(), new UTF8Encoding(false));
        Console.WriteLine(report);
        Console.WriteLine($"Modèle écrit : {Path.GetFullPath(output)}");
        return 0;
    }

    private static List<CalibrationSample> Collect(CaptureFile data, int temp, int power, int fan, double maxPowerStdDev) =>
        temp < 0 || power < 0 || fan < 0
            ? []
            : ThermalModelCalibrator.Windows(data.Time, data.Values[temp], data.Values[power], data.Values[fan], maxPowerStdDev: maxPowerStdDev);

    private static string Name(CaptureFile data, int index) => index < 0 ? "—" : $"`{data.Sensors[index].Id}`";

    private static void AppendResult(StringBuilder sb, string title, CalibrationResult? result, StaticThermalModel current, string? note)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"## {title}");
        sb.AppendLine();
        if (result is null)
        {
            sb.AppendLine("⚠️ Pas assez de fenêtres stables pour calibrer (au moins 10 fenêtres de 10 s à puissance stable).");
            sb.AppendLine();
            return;
        }

        var m = result.Model;
        sb.AppendLine("| | a (°C) | b (°C/W) | c (°C/%) | RMS |");
        sb.AppendLine("|---|---|---|---|---|");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| **Calibré** | {F(m.Intercept)} | {F(m.PowerGain, "0.000")} | {F(m.FanGain, "0.000")} | {F(result.RmsError, "0.00")} °C |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Actuel (code) | {F(current.Intercept)} | {F(current.PowerGain, "0.000")} | {F(current.FanGain, "0.000")} | |");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Fenêtres retenues : {result.SampleCount}, écartées près de la limite : {result.ExcludedNearLimit}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Variation de la ventilation : σ = {F(result.FanSpread)} %");
        sb.AppendLine(result.FanEffectIdentified
            ? "- ✅ Effet des ventilateurs identifié par les données."
            : $"- ⚠️ Effet des ventilateurs **non identifiable** (ventilation trop peu variée ou effet non physique) : c repris du modèle actuel. Faire un balayage de ventilation (σ ≥ {F(ThermalModelCalibrator.MinFanSpread, "0")} %).");
        if (note is not null)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"- {note}");
        }

        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Code : `new StaticThermalModel({F(m.Intercept, "0.0")}, {F(m.PowerGain, "0.000")}, {F(m.FanGain, "0.000")})`");
        sb.AppendLine();
    }

    private static string F(double value, string format = "0.0") => value.ToString(format, CultureInfo.InvariantCulture);
}
