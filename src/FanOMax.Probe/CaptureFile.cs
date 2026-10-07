using FanOMax.Core.Sensors;

namespace FanOMax.Probe;

/// <summary>Capture CSV produite par <c>record</c>, relue pour l'analyse et le calibrage.</summary>
internal sealed record CaptureFile(
    IReadOnlyList<(string Id, string Name)> Sensors,
    double[] Time,
    double?[] Fps,
    string[] FpsApp,
    double?[][] Values)
{
    private const int FirstSensorColumn = 4;

    public static CaptureFile Load(string path)
    {
        var lines = File.ReadAllLines(path);
        var header = Csv.SplitLine(lines[0]);
        var sensors = header.Skip(FirstSensorColumn)
            .Select(h => h.Split('|', 2))
            .Select(p => (Id: p[0], Name: p.Length > 1 ? p[1] : p[0]))
            .ToList();

        var rows = lines.Skip(1).Where(l => l.Length > 0).Select(Csv.SplitLine).ToList();
        var values = new double?[sensors.Count][];
        for (var c = 0; c < sensors.Count; c++)
        {
            var column = FirstSensorColumn + c;
            values[c] = rows.Select(r => column < r.Count ? Csv.ParseNumber(r[column]) : null).ToArray();
        }

        return new CaptureFile(
            sensors,
            rows.Select(r => Csv.ParseNumber(r[1]) ?? 0).ToArray(),
            rows.Select(r => Csv.ParseNumber(r[2])).ToArray(),
            rows.Select(r => r[3]).ToArray(),
            values);
    }

    /// <summary>Index d'un capteur clé, ou du premier capteur dont « identifiant|nom » contient <paramref name="overrideText"/>.</summary>
    public int Resolve(string? overrideText, KeySensor key) =>
        string.IsNullOrWhiteSpace(overrideText) ? KeySensorResolver.Find(Sensors, key) : Find(overrideText);

    /// <summary>Index du premier capteur dont « identifiant|nom » contient <paramref name="text"/>, ou -1.</summary>
    public int Find(string text) =>
        Enumerable.Range(0, Sensors.Count).FirstOrDefault(
            i => $"{Sensors[i].Id}|{Sensors[i].Name}".Contains(text, StringComparison.OrdinalIgnoreCase), -1);

    /// <summary>
    /// Premier contrôle PWM sous <paramref name="hardwarePrefix"/> dont le ventilateur associé (même index) tourne :
    /// c'est la consigne de ventilation la plus représentative pour le calibrage.
    /// </summary>
    public int FindActiveControl(string hardwarePrefix)
    {
        for (var i = 0; i < Sensors.Count; i++)
        {
            var id = Sensors[i].Id;
            if (!id.StartsWith(hardwarePrefix, StringComparison.OrdinalIgnoreCase) || SensorDescriptor.KindFromId(id) != SensorKind.Control)
            {
                continue;
            }

            var fanId = id.Replace("/control/", "/fan/", StringComparison.Ordinal);
            var fan = Enumerable.Range(0, Sensors.Count).FirstOrDefault(j => Sensors[j].Id == fanId, -1);
            if (fan >= 0 && Values[fan].Any(v => v > 0))
            {
                return i;
            }
        }

        return -1;
    }
}
