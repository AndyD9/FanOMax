namespace FanOMax.Core.Analysis;

/// <summary>Bilan d'un groupe sur une fenêtre du journal du service (une ligne par seconde).</summary>
/// <param name="AboveTarget">Part du temps au-dessus de la cible (%).</param>
/// <param name="Travel">Variation totale de la ventilation appliquée par minute (yoyo audible), en %/min.</param>
/// <param name="Statuses">Part du temps passé dans chaque état du régulateur (%), du plus fréquent au plus rare.</param>
public sealed record ShadowWindowStats(
    int Count,
    double? Target,
    SeriesStats? Temperature,
    double AboveTarget,
    SeriesStats? Applied,
    double Travel,
    IReadOnlyList<(string Status, double Share)> Statuses)
{
    public static ShadowWindowStats Compute(IReadOnlyList<ShadowRecord> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var target = rows.LastOrDefault(r => r.Target.HasValue)?.Target;
        var temperatures = rows.Where(r => r.Temperature.HasValue).Select(r => r.Temperature!.Value).ToList();
        var above = target is { } t && temperatures.Count > 0 ? 100.0 * temperatures.Count(v => v > t) / temperatures.Count : 0;
        var statuses = rows.Count == 0
            ? []
            : rows.GroupBy(r => r.Status, StringComparer.Ordinal)
                .Select(g => (g.Key, 100.0 * g.Count() / rows.Count))
                .OrderByDescending(s => s.Item2)
                .ToList();

        return new ShadowWindowStats(
            rows.Count,
            target,
            SeriesStats.Compute(rows.Select(r => r.Temperature)),
            above,
            SeriesStats.Compute(rows.Select(r => r.Applied)),
            ComputeTravel(rows.Select(r => r.Applied)),
            statuses);
    }

    /// <summary>Variation totale par minute, en supposant un échantillon par seconde (même calcul que <c>shadow-report</c>).</summary>
    public static double ComputeTravel(IEnumerable<double?> values)
    {
        var list = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return list.Count < 2 ? 0 : list.Zip(list.Skip(1), (a, b) => Math.Abs(b - a)).Sum() / (list.Count / 60.0);
    }
}
