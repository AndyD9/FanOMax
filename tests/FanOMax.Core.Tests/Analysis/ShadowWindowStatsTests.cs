using FanOMax.Core.Analysis;

namespace FanOMax.Core.Tests.Analysis;

public class ShadowWindowStatsTests
{
    private static ShadowRecord Row(int second, double temperature, double applied, string status = "Normal") =>
        new(new DateTime(2026, 10, 9, 9, 41, 0).AddSeconds(second), "CPU", "Active", true, 69, temperature, temperature, temperature,
            85, 85, 40, 0, applied, status, applied, true, "");

    [Fact]
    public void Compute_SummarizesTemperatureFanAndStatuses()
    {
        var rows = new[]
        {
            Row(0, 68, 40),
            Row(1, 70, 42),
            Row(2, 75, 50, "Protection"),
            Row(3, 66, 40),
        };

        var stats = ShadowWindowStats.Compute(rows);

        Assert.Equal(4, stats.Count);
        Assert.Equal(69, stats.Target);
        Assert.Equal(69.75, stats.Temperature!.Mean, precision: 10);
        Assert.Equal(75, stats.Temperature.Max);
        Assert.Equal(50, stats.AboveTarget, precision: 10);
        Assert.Equal(43, stats.Applied!.Mean, precision: 10);
        // |42−40| + |50−42| + |40−50| = 20 % sur 4 s → 300 %/min.
        Assert.Equal(300, stats.Travel, precision: 10);
        Assert.Equal([("Normal", 75.0), ("Protection", 25.0)], stats.Statuses);
    }

    [Fact]
    public void Compute_EmptyWindow_HasNoStats()
    {
        var stats = ShadowWindowStats.Compute([]);

        Assert.Equal(0, stats.Count);
        Assert.Null(stats.Temperature);
        Assert.Null(stats.Applied);
        Assert.Equal(0, stats.Travel);
        Assert.Empty(stats.Statuses);
    }
}
