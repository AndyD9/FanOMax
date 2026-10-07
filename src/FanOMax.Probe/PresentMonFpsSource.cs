using System.Diagnostics;
using FanOMax.Core.Analysis;

namespace FanOMax.Probe;

/// <summary>
/// Lit les FPS via PresentMon (traçage ETW de Windows, sans injection dans le jeu).
/// PresentMon écrit une ligne CSV par image présentée ; on compte les images par application sur 1 s.
/// </summary>
internal sealed class PresentMonFpsSource : IDisposable
{
    private const string SessionName = "FanOMaxProbe";

    // Composition du bureau et outils : jamais « le jeu ».
    private static readonly HashSet<string> IgnoredApplications = new(StringComparer.OrdinalIgnoreCase)
    {
        "dwm.exe",
        "PresentMon.exe",
    };

    private readonly string _exePath;
    private readonly Process _process;
    private readonly FpsCounter _counter = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Queue<string> _stderrTail = new();

    private PresentMonFpsSource(string exePath, Process process)
    {
        _exePath = exePath;
        _process = process;
    }

    public bool HasExited => _process.HasExited;

    /// <summary>Dernières lignes d'erreur de PresentMon, pour le diagnostic.</summary>
    public string ErrorTail
    {
        get
        {
            lock (_stderrTail)
            {
                return string.Join(Environment.NewLine, _stderrTail);
            }
        }
    }

    /// <summary>Cherche PresentMon : option explicite, sinon <c>tools\PresentMon*.exe</c>.</summary>
    public static string? Locate(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return File.Exists(explicitPath) ? Path.GetFullPath(explicitPath) : null;
        }

        var tools = Path.Combine(Environment.CurrentDirectory, "tools");
        return Directory.Exists(tools)
            ? Directory.EnumerateFiles(tools, "PresentMon*.exe").Order(StringComparer.OrdinalIgnoreCase).LastOrDefault()
            : null;
    }

    public static PresentMonFpsSource Start(string exePath)
    {
        var startInfo = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        // --stop_existing_session : nettoie une session ETW orpheline laissée par un arrêt brutal précédent.
        foreach (var arg in new[] { "--output_stdout", "--stop_existing_session", "--session_name", SessionName, "--no_track_input" })
        {
            startInfo.ArgumentList.Add(arg);
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Impossible de démarrer PresentMon.");
        var source = new PresentMonFpsSource(exePath, process);
        process.ErrorDataReceived += (_, e) => source.OnStderr(e.Data);
        process.BeginErrorReadLine();
        _ = Task.Run(source.ReadFramesAsync);
        return source;
    }

    /// <summary>Application au premier plan (celle qui présente le plus d'images) et ses FPS.</summary>
    public (string App, double Fps)? Current => _counter.GetTop(_clock.Elapsed);

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(3000);
            }

            // Ferme proprement la session ETW (sinon elle reste ouverte jusqu'au prochain lancement).
            using var cleanup = Process.Start(new ProcessStartInfo(_exePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "--terminate_existing_session", "--session_name", SessionName },
            });
            cleanup?.WaitForExit(5000);
        }
        catch (InvalidOperationException)
        {
            // Processus déjà terminé : rien à nettoyer.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Nettoyage au mieux : la prochaine session utilisera --stop_existing_session.
        }
        finally
        {
            _process.Dispose();
        }
    }

    private async Task ReadFramesAsync()
    {
        var reader = _process.StandardOutput;
        var appColumn = -1;

        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            var fields = line.Split(',');
            if (appColumn < 0)
            {
                appColumn = Array.FindIndex(fields, f => f.Trim('"').Equals("Application", StringComparison.OrdinalIgnoreCase));
                continue;
            }

            if (appColumn < fields.Length)
            {
                var app = fields[appColumn].Trim('"');
                if (app.Length > 0 && !IgnoredApplications.Contains(app))
                {
                    _counter.AddFrame(app, _clock.Elapsed);
                }
            }
        }
    }

    private void OnStderr(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_stderrTail)
        {
            _stderrTail.Enqueue(line);
            while (_stderrTail.Count > 10)
            {
                _stderrTail.Dequeue();
            }
        }
    }
}
