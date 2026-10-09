using FanOMax.Contracts;
using FanOMax.Service.Configuration;
using FanOMax.Service.Engine;
using Microsoft.Extensions.Logging.Abstractions;
using static FanOMax.Service.Tests.FakeBackend;

namespace FanOMax.Service.Tests;

public sealed class LiveStatusTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fanomax-live-");

    public void Dispose() => _directory.Delete(recursive: true);

    private static FanOMaxOptions Options(OperatingMode mode) => new()
    {
        Mode = mode,
        Groups =
        [
            new GroupOptions { Name = "CPU", Kind = GroupKind.Cpu, Controls = [Fan1, Fan2, Fan7], TargetTemperature = 69 },
            new GroupOptions { Name = "GPU", Kind = GroupKind.Gpu, Enabled = false, Controls = [GpuFan] },
        ],
    };

    private LiveSnapshot RunAndRead(FakeBackend backend, OperatingMode mode, int ticks)
    {
        using var engine = new RegulationEngine(backend, Options(mode), NullLogger.Instance, () => false, liveStatus: new LiveStatusFile(_directory.FullName));
        for (var i = 0; i < ticks; i++)
        {
            engine.Tick(1);
        }

        var snapshot = LiveSnapshot.FromJson(File.ReadAllText(Path.Combine(_directory.FullName, LiveSnapshot.FileName)));
        Assert.NotNull(snapshot);
        return snapshot;
    }

    [Fact]
    public void Active_ListsEveryFanWithItsSpeedAndTheTargetOfItsGroup()
    {
        var snapshot = RunAndRead(new FakeBackend(), OperatingMode.Active, 30);

        Assert.True(snapshot.Writing);
        var cpu = Assert.Single(snapshot.Groups);
        Assert.Equal(69, cpu.Target);

        // Chaque sortie PWM est listée, même celles que FanOMax ne pilote pas.
        Assert.Equal([Fan1, Fan2, Fan3, Fan4, Fan5, Fan7, GpuFan], snapshot.Fans.Select(f => f.ControlId));

        var fan1 = snapshot.Fans.Single(f => f.ControlId == Fan1);
        Assert.Equal("CPU", fan1.Group);
        Assert.Equal(cpu.Percent, fan1.TargetPercent);
        // Le % appliqué est lu en début de cycle : c'est la consigne du cycle précédent, très proche.
        Assert.InRange(fan1.Percent!.Value, cpu.Percent - 3, cpu.Percent + 3);
        Assert.Equal(1718, fan1.Rpm);
        Assert.True(fan1.Written);

        // Le GPU (groupe désactivé) reste au pilote AMD : pas de groupe ni de consigne, mais sa vitesse est connue.
        var gpu = snapshot.Fans.Single(f => f.ControlId == GpuFan);
        Assert.Null(gpu.Group);
        Assert.Null(gpu.TargetPercent);
        Assert.Equal(58, gpu.Percent);
        Assert.Equal(2850, gpu.Rpm);

        Assert.Contains(snapshot.Temperatures, t => t.Id == CpuTemp && t.Value == 66);
        Assert.Contains(snapshot.Temperatures, t => t.Id == GpuHotSpot && t.Value == 62);
    }

    [Fact]
    public void Shadow_ShowsTheTargetWithoutWriting()
    {
        var snapshot = RunAndRead(new FakeBackend(), OperatingMode.Shadow, 5);

        Assert.False(snapshot.Writing);
        var fan1 = snapshot.Fans.Single(f => f.ControlId == Fan1);
        Assert.Equal("CPU", fan1.Group);
        Assert.NotNull(fan1.TargetPercent);
        Assert.False(fan1.Written);
        Assert.Equal(48, fan1.Percent);
    }

    [Fact]
    public void MissingTemperature_IsLeftOut()
    {
        var backend = new FakeBackend();
        backend.Set(GpuHotSpot, null);

        var snapshot = RunAndRead(backend, OperatingMode.Shadow, 1);

        Assert.DoesNotContain(snapshot.Temperatures, t => t.Id == GpuHotSpot);
    }

    [Fact]
    public void WriteFailure_DoesNotStopTheRegulation()
    {
        // Le dossier disparaît : l'écriture échoue à chaque cycle, mais le moteur continue de réguler.
        var directory = Directory.CreateTempSubdirectory("fanomax-live-gone-");
        var live = new LiveStatusFile(directory.FullName);
        directory.Delete(recursive: true);
        var backend = new FakeBackend();
        using var engine = new RegulationEngine(backend, Options(OperatingMode.Active), NullLogger.Instance, () => false, liveStatus: live);

        for (var i = 0; i < 3; i++)
        {
            Assert.True(engine.Tick(1).Groups[0].Written);
        }
    }

    [Fact]
    public void FromJson_InvalidText_ReturnsNull()
    {
        Assert.Null(LiveSnapshot.FromJson(""));
        Assert.Null(LiveSnapshot.FromJson("{ \"Timestamp\": "));
    }
}
