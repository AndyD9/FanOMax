using System.Text;
using FanOMax.Contracts;
using FanOMax.Core.Sensors;

namespace FanOMax.Service.Engine;

/// <summary>
/// Instantané de chaque cycle pour l'interface (<c>live.json</c>) : groupes, chaque ventilateur (%, tr/min, consigne),
/// toutes les températures. Écriture atomique (fichier temporaire puis remplacement) : l'interface ne lit jamais
/// un fichier à moitié écrit.
/// </summary>
public sealed class LiveStatusFile
{
    private readonly string _path;
    private readonly string _temporary;

    public LiveStatusFile(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, LiveSnapshot.FileName);
        _temporary = _path + ".tmp";
    }

    /// <summary>Écrit l'instantané. Les erreurs d'écriture (disque plein…) sont propagées à l'appelant.</summary>
    public void Write(LiveSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        File.WriteAllText(_temporary, snapshot.ToJson(), new UTF8Encoding(false));
        File.Move(_temporary, _path, overwrite: true);
    }

    /// <summary>
    /// Construit l'instantané du cycle. Chaque sortie PWM (<c>…/control/n</c>) est associée au tachymètre de même numéro
    /// (<c>…/fan/n</c>), comme sur la Nuvoton et le GPU (identification du 2026-10-09).
    /// </summary>
    internal static LiveSnapshot Build(EngineTick tick, IReadOnlyList<SensorDescriptor> sensors, float?[] values, IReadOnlyList<GroupRuntime> groups)
    {
        double? Value(int i) => i >= 0 && i < values.Length && values[i] is { } v && float.IsFinite(v) ? v : null;

        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < sensors.Count; i++)
        {
            index.TryAdd(sensors[i].Id, i);
        }

        var ticks = tick.Groups.ToDictionary(g => g.Name, StringComparer.Ordinal);
        var owners = new Dictionary<string, (GroupRuntime Group, GroupTick Tick)>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            if (ticks.TryGetValue(group.Name, out var groupTick))
            {
                foreach (var id in group.ControlIds)
                {
                    owners.TryAdd(id, (group, groupTick));
                }
            }
        }

        var fans = new List<LiveFan>();
        var temperatures = new List<LiveTemperature>();
        for (var i = 0; i < sensors.Count; i++)
        {
            var sensor = sensors[i];
            if (sensor.Kind == SensorKind.Control)
            {
                var rpm = index.TryGetValue(sensor.Id.Replace("/control/", "/fan/", StringComparison.Ordinal), out var fanIndex) ? Value(fanIndex) : null;
                // Objectif propre à la sortie : consigne du groupe × facteur de la sortie (ControlScales).
                LiveFan fan = owners.TryGetValue(sensor.Id, out var owner)
                    ? new(sensor.Id, sensor.Name, sensor.HardwareName, Value(i), rpm, owner.Tick.Name,
                        owner.Group.OutputPercent(sensor.Id, owner.Tick.Percent, owner.Tick.Status), owner.Tick.Written)
                    : new(sensor.Id, sensor.Name, sensor.HardwareName, Value(i), rpm, null, null, false);
                fans.Add(fan);
            }
            else if (sensor.Kind == SensorKind.Temperature && Value(i) is { } temperature)
            {
                temperatures.Add(new LiveTemperature(sensor.Id, sensor.Name, sensor.HardwareName, temperature));
            }
        }

        var liveGroups = tick.Groups
            .Select(g => new LiveGroup(g.Name, g.Target, g.Temperature, g.Power, g.Percent, g.Status, g.Written))
            .ToList();
        return new LiveSnapshot(tick.Timestamp, tick.Mode.ToString(), tick.WritingAllowed, liveGroups, fans, temperatures);
    }
}
