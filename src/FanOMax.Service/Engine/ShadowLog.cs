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
        "timestamp,group,mode,writing,target,temperature,filtered_temperature,power,filtered_power,feedforward,correction,decision_percent,status,applied_percent,written,reason";

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
        var path = Path.Combine(_directory, $"shadow-{day.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.csv");
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

    private void Purge(DateOnly today)
    {
        var oldest = today.AddDays(-_retentionDays);
        foreach (var file in Directory.EnumerateFiles(_directory, "shadow-*.csv"))
        {
            var stamp = Path.GetFileNameWithoutExtension(file)["shadow-".Length..];
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
