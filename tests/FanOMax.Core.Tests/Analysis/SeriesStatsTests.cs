using FanOMax.Core.Analysis;

namespace FanOMax.Core.Tests.Analysis;

public class SeriesStatsTests
{
    [Fact]
    public void Compute_IgnoresMissingAndNonFiniteValues()
    {
        var stats = SeriesStats.Compute([1, null, 3, double.NaN, 2]);

        Assert.NotNull(stats);
        Assert.Equal(1, stats.Min);
        Assert.Equal(3, stats.Max);
        Assert.Equal(2, stats.Mean, precision: 10);
        Assert.Equal(3, stats.Count);
    }

    [Fact]
    public void Compute_ReturnsNull_WhenNoValue()
    {
        Assert.Null(SeriesStats.Compute([null, null]));
    }

    [Fact]
    public void Compute_P95_IsTheNearestRankPercentile()
    {
        var values = Enumerable.Range(1, 100).Select(v => (double?)v);

        Assert.Equal(95, SeriesStats.Compute(values)!.P95);
    }
}
