using FanOMax.Service.Configuration;
using FanOMax.Service.Engine;
using Microsoft.Extensions.Logging.Abstractions;
using static FanOMax.Service.Tests.FakeBackend;

namespace FanOMax.Service.Tests;

public class RegulationEngineTests
{
    private static FanOMaxOptions Options(OperatingMode mode) => new()
    {
        Mode = mode,
        Groups =
        [
            new GroupOptions { Name = "CPU", Kind = GroupKind.Cpu, Controls = [Fan1, Fan2, Fan7] },
            new GroupOptions { Name = "GPU", Kind = GroupKind.Gpu, Controls = [GpuFan] },
        ],
    };

    private static RegulationEngine Engine(FakeBackend backend, FanOMaxOptions options, Func<bool>? fanControlRunning = null) =>
        new(backend, options, NullLogger.Instance, fanControlRunning ?? (() => false));

    private static List<EngineTick> Run(RegulationEngine engine, int ticks) =>
        Enumerable.Range(0, ticks).Select(_ => engine.Tick(1)).ToList();

    [Fact]
    public void Shadow_NeverWritesNorRestores_WhateverHappens()
    {
        var backend = new FakeBackend();
        var engine = Engine(backend, Options(OperatingMode.Shadow));

        Run(engine, 30);
        backend.Set(CpuTemp, null);
        var lost = Run(engine, 20);
        backend.Set(CpuTemp, 95);
        var critical = Run(engine, 10);
        engine.HandBackAll("test");
        engine.Dispose();

        // Les décisions sont bien calculées…
        Assert.Contains(lost, t => t.Groups[0].Status == "SensorLost");
        Assert.Equal("Critical", critical[^1].Groups[0].Status);
        Assert.All(lost.Concat(critical), t => Assert.False(t.WritingAllowed));

        // …mais rien n'est jamais écrit ni rendu au BIOS : FanControl garde la main.
        Assert.Empty(backend.Writes);
        Assert.Empty(backend.Restores);
    }

    [Fact]
    public void Shadow_ReportsWhatIsActuallyApplied()
    {
        var backend = new FakeBackend();
        var engine = Engine(backend, Options(OperatingMode.Shadow));

        var tick = engine.Tick(1);

        Assert.Equal(48, tick.Groups[0].AppliedPercent);
        Assert.Equal(58, tick.Groups[1].AppliedPercent);
    }

    [Fact]
    public void Active_WritesTheDecisionToEveryControlOfTheGroup()
    {
        var backend = new FakeBackend();
        var engine = Engine(backend, Options(OperatingMode.Active));

        var tick = engine.Tick(1);

        Assert.True(tick.WritingAllowed);
        var cpu = tick.Groups[0];
        Assert.Equal([Fan1, Fan2, Fan7, GpuFan], backend.Writes.Select(w => w.Id));
        Assert.All(backend.Writes.Take(3), w => Assert.Equal(cpu.Percent, w.Percent));
        Assert.DoesNotContain(backend.Writes, w => w.Id == Fan3);
        Assert.Equal(4, engine.WrittenControls.Count);
    }

    [Fact]
    public void Active_RefusesToWrite_WhileFanControlIsRunning()
    {
        var backend = new FakeBackend();
        var engine = Engine(backend, Options(OperatingMode.Active), () => true);

        var ticks = Run(engine, 30);

        Assert.All(ticks, t => Assert.False(t.WritingAllowed));
        Assert.All(ticks, t => Assert.True(t.FanControlRunning));
        Assert.Empty(backend.Writes);
    }

    [Fact]
    public void Active_StopsWritingWithoutRestoring_WhenFanControlStarts()
    {
        var backend = new FakeBackend();
        var fanControl = false;
        var engine = Engine(backend, Options(OperatingMode.Active), () => fanControl);
        Run(engine, 5);
        var writesBefore = backend.Writes.Count;

        fanControl = true;
        Run(engine, 15);
        var writesAfterDetection = backend.Writes.Count;
        Run(engine, 10);
        engine.Dispose();

        // Détection au plus tard 10 s après, puis plus aucune écriture ; et surtout aucun retour au BIOS,
        // qui retirerait la main à FanControl.
        Assert.True(writesBefore > 0);
        Assert.Equal(writesAfterDetection, backend.Writes.Count);
        Assert.Empty(backend.Restores);
        Assert.Empty(engine.WrittenControls);
    }

    [Fact]
    public void Active_LostSensor_HandsTheGroupBackToBios_ThenResumes()
    {
        var backend = new FakeBackend();
        var options = Options(OperatingMode.Active);
        var engine = Engine(backend, options);
        Run(engine, 5);

        backend.Set(CpuTemp, null);
        var lost = Run(engine, 10);

        Assert.Contains(lost, t => t.Groups[0].Status == "Bios");
        Assert.Equal([Fan1, Fan2, Fan7], backend.Restores.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(GpuFan, backend.Restores);

        // Retour des valeurs : FanOMax attend ResumeAfterSeconds avant de reprendre la main.
        backend.Set(CpuTemp, 66);
        backend.Writes.Clear();
        var waiting = Run(engine, (int)options.Safety.ResumeAfterSeconds - 1);
        Assert.All(waiting, t => Assert.Equal("Bios", t.Groups[0].Status));
        Assert.DoesNotContain(backend.Writes, w => w.Id == Fan1);

        var resumed = Run(engine, 2);
        Assert.NotEqual("Bios", resumed[^1].Groups[0].Status);
        Assert.Contains(backend.Writes, w => w.Id == Fan1);
        Assert.Equal(3, backend.Restores.Count);
    }

    [Fact]
    public void Active_CriticalTemperature_ForcesFullSpeed()
    {
        var backend = new FakeBackend();
        var engine = Engine(backend, Options(OperatingMode.Active));
        Run(engine, 5);
        backend.Writes.Clear();

        backend.Set(CpuTemp, 92);
        var tick = engine.Tick(1);

        Assert.Equal("Critical", tick.Groups[0].Status);
        Assert.All(backend.Writes.Where(w => w.Id != GpuFan), w => Assert.Equal(100, w.Percent));
    }

    [Fact]
    public void Dispose_HandsBackOnlyTheControlsFanOMaxWrote()
    {
        var backend = new FakeBackend();
        var engine = Engine(backend, Options(OperatingMode.Active));
        Run(engine, 3);

        engine.Dispose();

        Assert.Equal(new[] { Fan1, Fan2, Fan7, GpuFan }.Order(StringComparer.Ordinal), backend.Restores.Order(StringComparer.Ordinal));
        Assert.Empty(engine.WrittenControls);
    }

    [Fact]
    public void Reconfigure_FromActiveToShadow_HandsBackAndStopsWriting()
    {
        var backend = new FakeBackend();
        var engine = Engine(backend, Options(OperatingMode.Active));
        Run(engine, 3);
        var writes = backend.Writes.Count;

        Assert.True(engine.Reconfigure(Options(OperatingMode.Shadow)));
        Run(engine, 5);

        Assert.Equal(4, backend.Restores.Count);
        Assert.Equal(writes, backend.Writes.Count);
    }

    [Fact]
    public void Reconfigure_RemovingAGroup_HandsBackItsControls()
    {
        var backend = new FakeBackend();
        var engine = Engine(backend, Options(OperatingMode.Active));
        Run(engine, 3);

        var cpuOnly = Options(OperatingMode.Active);
        cpuOnly.Groups.RemoveAt(1);
        engine.Reconfigure(cpuOnly);

        Assert.Equal([GpuFan], backend.Restores);
    }

    [Fact]
    public void Reconfigure_InvalidOptions_KeepsTheCurrentConfiguration()
    {
        var backend = new FakeBackend();
        var engine = Engine(backend, Options(OperatingMode.Shadow));
        var invalid = Options(OperatingMode.Active);
        invalid.Groups[1].Controls.Add(Fan1);

        Assert.False(engine.Reconfigure(invalid));
        Assert.NotEmpty(engine.ConfigurationErrors);
        Assert.Equal(OperatingMode.Shadow, engine.Options.Mode);
        Assert.Equal(["CPU", "GPU"], engine.GroupNames);
    }

    [Fact]
    public void Reconfigure_UnknownControl_DisablesOnlyThatGroup()
    {
        var backend = new FakeBackend();
        var options = Options(OperatingMode.Shadow);
        options.Groups[1].Controls = ["/gpu-nvidia/0/control/0"];

        var engine = Engine(backend, options);

        Assert.Equal(["CPU"], engine.GroupNames);
        Assert.Contains(engine.ConfigurationErrors, e => e.Contains("GPU", StringComparison.Ordinal));
    }

    [Fact]
    public void Tick_PropagatesHardwareErrors_ToTheService()
    {
        var backend = new FakeBackend { UpdateFailure = new InvalidOperationException("capteur en panne") };
        var engine = Engine(backend, Options(OperatingMode.Shadow));

        Assert.Throws<InvalidOperationException>(() => engine.Tick(1));
    }

    [Fact]
    public void ProfileAndTargetOverride_AreApplied()
    {
        var backend = new FakeBackend();
        var options = Options(OperatingMode.Shadow);
        options.Profile = Core.Control.FanProfile.Perf;
        options.Groups[1].TargetTemperature = 80;

        var tick = Engine(backend, options).Tick(1);

        Assert.Equal(65, tick.Groups[0].Target);
        Assert.Equal(80, tick.Groups[1].Target);
    }
}
