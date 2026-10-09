using FanOMax.Service.Configuration;
using FanOMax.Service.Engine;
using Microsoft.Extensions.Logging.Abstractions;
using static FanOMax.Service.Tests.FakeBackend;

namespace FanOMax.Service.Tests;

/// <summary>Incidents réels du mode fantôme (2026-10-08) : disque plein et réveil après une veille.</summary>
public sealed class ResilienceTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fanomax-resilience-");

    public void Dispose()
    {
        if (_directory.Exists)
        {
            _directory.Delete(recursive: true);
        }
    }

    private static FanOMaxOptions Options(OperatingMode mode) => new()
    {
        Mode = mode,
        Groups =
        [
            new GroupOptions { Name = "CPU", Kind = GroupKind.Cpu, Controls = [Fan1, Fan2, Fan7] },
            new GroupOptions { Name = "GPU", Kind = GroupKind.Gpu, Controls = [GpuFan] },
        ],
    };

    [Fact]
    public void ShadowLogFailure_DoesNotInterruptRegulation_AndIsRetried()
    {
        var backend = new FakeBackend();
        var path = Path.Combine(_directory.FullName, "shadow-20261009.csv");
        File.WriteAllText(path, ShadowLog.Header + Environment.NewLine);

        // Fichier du jour inaccessible (comme un disque plein) : verrouillé en exclusivité par le test.
        var lockHandle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var engine = new RegulationEngine(
            backend, Options(OperatingMode.Active), NullLogger.Instance, () => false,
            new ShadowLog(_directory.FullName, 7), () => new DateTime(2026, 10, 9, 12, 0, 0));

        for (var i = 0; i < 30; i++)
        {
            engine.Tick(1);
        }

        // La régulation continue : écritures PWM à chaque cycle, rien n'est rendu au BIOS.
        Assert.True(engine.ShadowLogFailing);
        Assert.Equal(30 * 4, backend.Writes.Count);
        Assert.Empty(backend.Restores);

        // Le fichier redevient accessible : le journal reprend au plus tard 60 s après l'échec.
        lockHandle.Dispose();
        for (var i = 0; i < 61; i++)
        {
            engine.Tick(1);
        }

        Assert.False(engine.ShadowLogFailing);
        engine.Dispose();
        Assert.True(File.ReadAllLines(path).Length > 1);
    }

    [Fact]
    public void ResetRegulators_ClearsTheAccumulatedCorrection()
    {
        var backend = new FakeBackend();
        backend.Set(CpuTemp, 75);
        var engine = new RegulationEngine(backend, Options(OperatingMode.Shadow), NullLogger.Instance, () => false);
        for (var i = 0; i < 300; i++)
        {
            engine.Tick(1);
        }

        var before = engine.Tick(1).Groups[0].Decision.Correction;
        engine.ResetRegulators("test");
        var after = engine.Tick(1).Groups[0].Decision.Correction;

        Assert.True(Math.Abs(after) < Math.Abs(before), $"avant {before:0.0}, après {after:0.0}");
    }

    [Theory]
    [InlineData(1.0, 1, false)]      // cycle normal
    [InlineData(3.5, 1, false)]      // cycle lent (machine chargée)
    [InlineData(83298, 1, true)]     // réveil après 23 h de veille (incident réel)
    [InlineData(12, 1, true)]
    [InlineData(12, 5, false)]       // intervalle de 5 s : 12 s reste un retard plausible
    [InlineData(30, 5, true)]
    public void IsSuspendGap_DistinguishesSleepFromSlowCycles(double elapsed, double expected, bool suspend)
    {
        Assert.Equal(suspend, LoopTiming.IsSuspendGap(elapsed, expected));
    }
}
