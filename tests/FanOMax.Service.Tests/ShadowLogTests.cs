using FanOMax.Core.Control;
using FanOMax.Service.Configuration;
using FanOMax.Service.Engine;

namespace FanOMax.Service.Tests;

public sealed class ShadowLogTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fanomax-shadow-");

    public void Dispose() => _directory.Delete(recursive: true);

    private static EngineTick Tick(DateTime timestamp) => new(
        timestamp,
        OperatingMode.Shadow,
        WritingAllowed: false,
        FanControlRunning: true,
        [
            new GroupTick("CPU", 67, 69.5, 88, new RegulatorDecision(63.4, RegulatorMode.Normal, 69.1, 87.2, 60, 3.4, null), 63.4, "Normal", 48.6, false),
            new GroupTick("GPU", 82, 61, 126, new RegulatorDecision(30, RegulatorMode.SensorHolding, 60.8, 125, 30, 0, "Puissance : valeur absente"), 30, "SensorHolding", 58, false),
        ]);

    private string[] Lines(string day) =>
        File.ReadAllLines(Path.Combine(_directory.FullName, $"shadow-{day}.csv"));

    [Fact]
    public void Write_AddsAHeaderAndOneLinePerGroup()
    {
        using (var log = new ShadowLog(_directory.FullName, 7))
        {
            log.Write(Tick(new DateTime(2026, 10, 7, 14, 0, 0)));
            log.Write(Tick(new DateTime(2026, 10, 7, 14, 0, 1)));
        }

        var lines = Lines("20261007");
        Assert.Equal(ShadowLog.Header, lines[0]);
        Assert.Equal(5, lines.Length);
        Assert.Equal("2026-10-07T14:00:00.000,\"CPU\",Shadow,0,67,69.5,69.1,88,87.2,60,3.4,63.4,Normal,48.6,0,", lines[1]);
        Assert.EndsWith(",SensorHolding,58,0,\"Puissance : valeur absente\"", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void Write_StartsANewFileEachDay_AndPurgesOldFiles()
    {
        var old = Path.Combine(_directory.FullName, "shadow-20260901.csv");
        File.WriteAllText(old, ShadowLog.Header);

        using (var log = new ShadowLog(_directory.FullName, retentionDays: 7))
        {
            log.Write(Tick(new DateTime(2026, 10, 7, 23, 59, 59)));
            log.Write(Tick(new DateTime(2026, 10, 8, 0, 0, 0)));
        }

        Assert.Equal(3, Lines("20261007").Length);
        Assert.Equal(3, Lines("20261008").Length);
        Assert.False(File.Exists(old));
    }

    [Fact]
    public void Write_AppendsToTheExistingFileOfTheDay_AfterARestart()
    {
        using (var log = new ShadowLog(_directory.FullName, 7))
        {
            log.Write(Tick(new DateTime(2026, 10, 7, 10, 0, 0)));
        }

        using (var log = new ShadowLog(_directory.FullName, 7))
        {
            log.Write(Tick(new DateTime(2026, 10, 7, 11, 0, 0)));
        }

        var lines = Lines("20261007");
        Assert.Equal(5, lines.Length);
        Assert.Single(lines, l => l == ShadowLog.Header);
    }
}
