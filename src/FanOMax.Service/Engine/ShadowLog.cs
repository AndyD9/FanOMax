using System.Globalization;
using System.Text;

namespace FanOMax.Service.Engine;

/// <summary>
/// Journal des décisions, une ligne par groupe et par cycle, dans un CSV par jour (<c>shadow-AAAAMMJJ.csv</c>).
/// En mode fantôme, il permet de comparer les décisions de FanOMax à ce que FanControl applique réellement.
/// </summary>
public sealed class ShadowLog : IDisposable
{
    public const string Header =
        "timestamp,group,mode,writing,target,temperature,regulator_temperature,filtered_temperature,power,filtered_power,feedforward,correction,decision_percent,status,applied_percent,written,reason";

    private readonly string _directory;
    private readonly int _retentionDays;
    private StreamWriter? _writer;
    private DateOnly _day;
    private int _pending;

    public ShadowLog(string directory, int retentionDays)
    {
        _directory = directory;
        _retentionDays = retentionDays;
        Directory.CreateDirectory(directory);
    }

    public void Write(EngineTick tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        EnsureFile(DateOnly.FromDateTime(tick.Timestamp));

        foreach (var g in tick.Groups)
        {
            var d = g.Decision;
            var line = string.Join(',',
                tick.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture),
                Quote(g.Name),
                tick.Mode,
                tick.WritingAllowed ? "1" : "0",
                N(g.Target),
                N(g.Temperature),
                N(g.RegulatorTemperature),
                N(d.FilteredTemperature),
                N(g.Power),
                N(d.FilteredPower),
                N(d.Feedforward),
                N(d.Correction),
                N(g.Percent),
                g.Status,
                N(g.AppliedPercent),
                g.Written ? "1" : "0",
                d.Reason is null ? "" : Quote(d.Reason));
            _writer!.WriteLine(line);
        }

        // Écriture par paquets : une écriture disque toutes les 10 lignes environ.
        if (++_pending >= 10)
        {
            _writer!.Flush();
            _pending = 0;
        }
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _writer = null;
    }

    private void EnsureFile(DateOnly day)
    {
        if (_writer is not null && day == _day)
        {
            return;
        }

        _writer?.Dispose();
        _day = day;
        var path = PathFor(day);
        var isNew = !File.Exists(path);

        // Partage en lecture : la sonde (shadow-report) peut lire le fichier du jour pendant l'écriture.
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream, new UTF8Encoding(false));
        if (isNew)
        {
            _writer.WriteLine(Header);
        }

        Purge(day);
    }

    /// <summary>
    /// Fichier du jour. Si un fichier du jour existe avec un autre format (mise à jour du service),
    /// on passe à <c>shadow-AAAAMMJJ-2.csv</c>, <c>-3</c>… plutôt que de mélanger deux formats.
    /// </summary>
    private string PathFor(DateOnly day)
    {
        var stamp = day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var path = Path.Combine(_directory, $"shadow-{stamp}.csv");
        for (var n = 2; File.Exists(path) && FirstLine(path) != Header; n++)
        {
            path = Path.Combine(_directory, $"shadow-{stamp}-{n}.csv");
        }

        return path;
    }

    private static string? FirstLine(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadLine();
    }

    private void Purge(DateOnly today)
    {
        var oldest = today.AddDays(-_retentionDays);
        foreach (var file in Directory.EnumerateFiles(_directory, "shadow-*.csv"))
        {
            // shadow-AAAAMMJJ.csv ou shadow-AAAAMMJJ-n.csv : la date est toujours sur les 8 premiers caractères.
            var name = Path.GetFileNameWithoutExtension(file)["shadow-".Length..];
            var stamp = name.Length >= 8 ? name[..8] : name;
            if (DateOnly.TryParseExact(stamp, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day < oldest)
            {
                File.Delete(file);
            }
        }
    }

    private static string N(double? value) =>
        value is { } v && double.IsFinite(v) ? v.ToString("0.##", CultureInfo.InvariantCulture) : "";

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
