namespace FanOMax.Core.Analysis;

/// <summary>Statistiques descriptives d'une série (les valeurs manquantes sont ignorées).</summary>
public sealed record SeriesStats(double Min, double Mean, double Max, double P95, int Count)
{
    public static SeriesStats? Compute(IEnumerable<double?> values)
    {
        var sorted = values.Where(v => v.HasValue && double.IsFinite(v.Value)).Select(v => v!.Value).Order().ToArray();
        if (sorted.Length == 0)
        {
            return null;
        }

        var p95Index = (int)Math.Ceiling(0.95 * sorted.Length) - 1;
        return new SeriesStats(sorted[0], sorted.Average(), sorted[^1], sorted[Math.Clamp(p95Index, 0, sorted.Length - 1)], sorted.Length);
    }
}
