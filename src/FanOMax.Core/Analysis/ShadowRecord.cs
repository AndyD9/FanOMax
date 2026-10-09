using System.Globalization;

namespace FanOMax.Core.Analysis;

/// <summary>
/// Une ligne du journal du service (<c>shadow-AAAAMMJJ.csv</c>) : un groupe pendant un cycle.
/// Les colonnes absentes des anciens formats sont <c>null</c>.
/// </summary>
/// <param name="Decision">Consigne de FanOMax (%).</param>
/// <param name="Applied">% réellement appliqué aux sorties (FanOMax, FanControl ou BIOS).</param>
public sealed record ShadowRecord(
    DateTime Time,
    string Group,
    string Mode,
    bool Writing,
    double? Target,
    double? Temperature,
    double? RegulatorTemperature,
    double? FilteredTemperature,
    double? Power,
    double? FilteredPower,
    double? Feedforward,
    double? Correction,
    double? Decision,
    string Status,
    double? Applied,
    bool Written,
    string Reason);

/// <summary>Lecture des lignes du journal selon l'en-tête du fichier (les colonnes sont repérées par leur nom).</summary>
public sealed class ShadowLogParser
{
    private readonly Dictionary<string, int> _columns;
    private readonly int _time;
    private readonly int _group;

    public ShadowLogParser(string headerLine)
    {
        ArgumentNullException.ThrowIfNull(headerLine);
        var names = Csv.SplitLine(headerLine);
        _columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < names.Count; i++)
        {
            _columns.TryAdd(names[i].Trim(), i);
        }

        _time = Index("timestamp");
        _group = Index("group");
    }

    /// <summary>Vrai si la ligne est un en-tête de journal.</summary>
    public static bool IsHeader(string line) => line.StartsWith("timestamp,", StringComparison.Ordinal);

    /// <summary>Ligne lue, ou <c>null</c> si elle est incomplète ou illisible.</summary>
    public ShadowRecord? Parse(string line)
    {
        if (_time < 0 || _group < 0)
        {
            return null;
        }

        var f = Csv.SplitLine(line);
        if (f.Count < _columns.Count
            || !DateTime.TryParse(f[_time], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return null;
        }

        string Text(string name) => Index(name) is var i and >= 0 ? f[i] : "";
        double? Number(string name) => Index(name) is var i and >= 0 ? Csv.ParseNumber(f[i]) : null;

        return new ShadowRecord(
            time,
            f[_group],
            Text("mode"),
            Text("writing") == "1",
            Number("target"),
            Number("temperature"),
            Number("regulator_temperature"),
            Number("filtered_temperature"),
            Number("power"),
            Number("filtered_power"),
            Number("feedforward"),
            Number("correction"),
            Number("decision_percent"),
            Text("status"),
            Number("applied_percent"),
            Text("written") == "1",
            Text("reason"));
    }

    private int Index(string name) => _columns.TryGetValue(name, out var i) ? i : -1;
}
