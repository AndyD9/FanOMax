using System.Diagnostics;
using System.Globalization;
using System.Text;
using FanOMax.Core.Sensors;
using FanOMax.Hardware;

namespace FanOMax.Probe;

/// <summary>Enregistre les capteurs dans un CSV à intervalle fixe (1 s par défaut). Lecture seule.</summary>
internal static class RecordCommand
{
    // Les fréquences et tensions multiplieraient les colonnes sans servir à la régulation.
    private static readonly HashSet<SensorKind> RecordedKinds =
        [SensorKind.Temperature, SensorKind.Power, SensorKind.Load, SensorKind.Fan, SensorKind.Control];

    public static async Task<int> RunAsync(CommandLine cli)
    {
        var label = cli.Get("label") ?? "capture";
        var duration = cli.GetDuration("duration");
        var interval = cli.GetDuration("interval") ?? TimeSpan.FromSeconds(1);
        var outputDir = cli.Get("out") ?? "captures";

        Directory.CreateDirectory(outputDir);
        var path = Path.Combine(outputDir, $"{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{label}.csv");

        using var monitor = LhmMonitor.Open();
        var keys = ResolveKeys(monitor.Sensors);

        // Parmi les fréquences, seule la fréquence CPU effective moyenne est utile (bridage thermique).
        var cpuClock = keys.GetValueOrDefault(KeySensor.CpuClock, -1);
        var columns = Enumerable.Range(0, monitor.Sensors.Count)
            .Where(i => RecordedKinds.Contains(monitor.Sensors[i].Kind) || i == cpuClock)
            .ToArray();

        using var fps = StartFps(cli);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        if (duration is { } d)
        {
            cts.CancelAfter(d);
        }

        await using var writer = new StreamWriter(path, append: false, new UTF8Encoding(false));
        await writer.WriteLineAsync(BuildHeader(monitor.Sensors, columns)).ConfigureAwait(false);

        Console.WriteLine($"Enregistrement « {label} » → {Path.GetFullPath(path)}");
        Console.WriteLine($"{columns.Length} capteurs, toutes les {interval.TotalSeconds:0.##} s, {(duration is null ? "jusqu'à Ctrl+C" : $"pendant {duration.Value:hh\\:mm\\:ss}")}.");
        if (SystemChecks.IsFanControlRunning())
        {
            Console.WriteLine("FanControl tourne : les % PWM enregistrés sont ceux qu'il impose.");
        }

        Console.WriteLine();

        var clock = Stopwatch.StartNew();
        using var timer = new PeriodicTimer(interval);
        var rows = 0;

        try
        {
            do
            {
                monitor.Update();
                var values = monitor.ReadAll();
                var current = fps?.Current;

                var row = new StringBuilder();
                row.Append(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture));
                row.Append(',').Append(Csv.Number(clock.Elapsed.TotalSeconds, "0.000"));
                row.Append(',').Append(Csv.Number(current?.Fps, "0"));
                row.Append(',').Append(current is null ? "" : Csv.Quote(current.Value.App));
                foreach (var i in columns)
                {
                    row.Append(',').Append(Csv.Number(values[i]));
                }

                await writer.WriteLineAsync(row.ToString()).ConfigureAwait(false);
                if (++rows % 10 == 0)
                {
                    await writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                }

                PrintStatus(clock.Elapsed, values, keys, current);

                if (fps is { HasExited: true })
                {
                    Console.WriteLine();
                    Console.WriteLine($"⚠️ PresentMon s'est arrêté, les FPS ne sont plus enregistrés. {fps.ErrorTail}");
                }
            }
            while (await timer.WaitForNextTickAsync(cts.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Fin normale : durée écoulée ou Ctrl+C.
        }

        await writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine($"{rows} lignes enregistrées dans {Path.GetFullPath(path)}");
        Console.WriteLine($"Analyse : dotnet run --project src\\FanOMax.Probe -- analyze \"{path}\"");
        return 0;
    }

    private static PresentMonFpsSource? StartFps(CommandLine cli)
    {
        if (!cli.Has("fps"))
        {
            return null;
        }

        var exe = PresentMonFpsSource.Locate(cli.Get("presentmon"));
        if (exe is null)
        {
            Console.WriteLine("⚠️ PresentMon introuvable (tools\\PresentMon*.exe ou --presentmon <chemin>) : enregistrement sans FPS.");
            return null;
        }

        Console.WriteLine($"FPS : PresentMon ({Path.GetFileName(exe)})");
        return PresentMonFpsSource.Start(exe);
    }

    private static string BuildHeader(IReadOnlyList<SensorDescriptor> sensors, int[] columns)
    {
        var header = new StringBuilder("timestamp,elapsed_s,fps,fps_app");
        foreach (var i in columns)
        {
            // Format « identifiant|nom » : l'identifiant donne le type, le nom reste lisible.
            header.Append(',').Append(Csv.Quote($"{sensors[i].Id}|{sensors[i].Name}"));
        }

        return header.ToString();
    }

    private static Dictionary<KeySensor, int> ResolveKeys(IReadOnlyList<SensorDescriptor> sensors)
    {
        var ids = sensors.Select(s => (s.Id, s.Name)).ToList();
        return Enum.GetValues<KeySensor>()
            .Select(k => (Key: k, Index: KeySensorResolver.Find(ids, k)))
            .Where(x => x.Index >= 0)
            .ToDictionary(x => x.Key, x => x.Index);
    }

    private static void PrintStatus(TimeSpan elapsed, float?[] values, Dictionary<KeySensor, int> keys, (string App, double Fps)? fps)
    {
        string V(KeySensor key, string unit) =>
            keys.TryGetValue(key, out var i) && values[i] is { } v ? $"{v.ToString("0", CultureInfo.InvariantCulture)}{unit}" : "—";

        var line = $"{elapsed:hh\\:mm\\:ss}  CPU {V(KeySensor.CpuTemperature, "°C")} {V(KeySensor.CpuPower, "W")} {V(KeySensor.CpuLoad, "%")} {V(KeySensor.CpuClock, "MHz")}"
                   + $"  |  GPU {V(KeySensor.GpuTemperature, "°C")} {V(KeySensor.GpuPower, "W")} {V(KeySensor.GpuLoad, "%")}"
                   + (fps is { } f ? $"  |  {f.Fps:0} FPS ({f.App})" : "");

        if (Console.IsOutputRedirected)
        {
            Console.WriteLine(line);
        }
        else
        {
            Console.Write("\r" + line.PadRight(Math.Max(0, Console.BufferWidth - 1)));
        }
    }
}
