using System.Globalization;
using System.Text;

namespace FanOMax.Probe;

/// <summary>Lecture et écriture CSV minimales (séparateur virgule, nombres au format invariant).</summary>
internal static class Csv
{
    public static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    public static string Number(double? value, string format = "0.###") =>
        value is { } v && double.IsFinite(v) ? v.ToString(format, CultureInfo.InvariantCulture) : "";

    public static double? ParseNumber(string field) =>
        double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    /// <summary>Découpe une ligne CSV en tenant compte des guillemets.</summary>
    public static List<string> SplitLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
