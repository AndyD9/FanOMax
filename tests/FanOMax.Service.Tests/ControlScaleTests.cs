using System.Text;
using FanOMax.Contracts;
using FanOMax.Service.Configuration;
using FanOMax.Service.Engine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using static FanOMax.Service.Tests.FakeBackend;

namespace FanOMax.Service.Tests;

/// <summary>Facteur par sortie (<see cref="GroupOptions.ControlScales"/>) : équilibre admission / extraction du boîtier.</summary>
public class ControlScaleTests
{
    // Machine réelle : ventirad, haut, hub avant (Fan #4, admission), arrière ; l'avant reçoit 60 % de la consigne.
    private static FanOMaxOptions Options() => new()
    {
        Mode = OperatingMode.Active,
        Groups =
        [
            new GroupOptions
            {
                Name = "CPU",
                Kind = GroupKind.Cpu,
                Controls = [Fan1, Fan2, Fan4, Fan7],
                ControlScales = new(StringComparer.Ordinal) { [Fan4] = 0.6 },
            },
        ],
    };

    private static RegulationEngine Engine(FakeBackend backend, FanOMaxOptions options) =>
        new(backend, options, NullLogger.Instance, () => false);

    private static double LastWrite(FakeBackend backend, string id) => backend.Writes.Last(w => w.Id == id).Percent;

    [Fact]
    public void Active_ScalesOnlyTheListedOutput()
    {
        var backend = new FakeBackend();
        backend.Set(CpuTemp, 75);
        backend.Set(CpuPower, 120);
        using var engine = Engine(backend, Options());

        EngineTick tick = null!;
        for (var i = 0; i < 60; i++)
        {
            tick = engine.Tick(1);
        }

        var percent = tick.Groups[0].Percent;
        Assert.InRange(percent, 50, 100);
        Assert.Equal(percent, LastWrite(backend, Fan1));
        Assert.Equal(percent, LastWrite(backend, Fan7));
        Assert.Equal(percent * 0.6, LastWrite(backend, Fan4), precision: 6);

        // Le % appliqué du groupe se lit sur les sorties sans facteur : il reste comparable à la consigne.
        Assert.InRange(tick.Groups[0].AppliedPercent!.Value, percent - 3, percent + 3);
    }

    [Fact]
    public void ScaledOutput_NeverGoesBelowTwentyPercent()
    {
        var backend = new FakeBackend();
        backend.Set(CpuTemp, 40);
        backend.Set(CpuPower, 30);
        using var engine = Engine(backend, Options());

        for (var i = 0; i < 60; i++)
        {
            engine.Tick(1);
        }

        // Repos : consigne au minimum (25 %) ; 25 × 0,6 = 15 % serait trop bas pour un ventilateur.
        Assert.Equal(25, LastWrite(backend, Fan1), precision: 0);
        Assert.Equal(GroupRuntime.MinScaledPercent, LastWrite(backend, Fan4));
    }

    [Fact]
    public void CriticalTemperature_PutsEveryOutputAtFullSpeed()
    {
        var backend = new FakeBackend();
        using var engine = Engine(backend, Options());
        engine.Tick(1);

        backend.Set(CpuTemp, 95);
        var tick = engine.Tick(1);

        Assert.Equal("Critical", tick.Groups[0].Status);
        Assert.All(new[] { Fan1, Fan2, Fan4, Fan7 }, id => Assert.Equal(100, LastWrite(backend, id)));
    }

    [Fact]
    public void LiveSnapshot_ShowsTheTargetOfEachOutput()
    {
        var directory = Directory.CreateTempSubdirectory("fanomax-scale-");
        try
        {
            var backend = new FakeBackend();
            using (var engine = new RegulationEngine(backend, Options(), NullLogger.Instance, () => false, liveStatus: new LiveStatusFile(directory.FullName)))
            {
                for (var i = 0; i < 30; i++)
                {
                    engine.Tick(1);
                }
            }

            var snapshot = LiveSnapshot.FromJson(File.ReadAllText(Path.Combine(directory.FullName, LiveSnapshot.FileName)))!;
            var percent = snapshot.Groups[0].Percent;
            Assert.Equal(percent, snapshot.Fans.Single(f => f.ControlId == Fan1).TargetPercent);
            Assert.Equal(Math.Max(percent * 0.6, GroupRuntime.MinScaledPercent), snapshot.Fans.Single(f => f.ControlId == Fan4).TargetPercent!.Value, precision: 6);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Configuration_BindsScalesFromJson()
    {
        const string json = """
            { "FanOMax": { "Groups": [ {
                "Name": "CPU",
                "Controls": [ "/lpc/nct6796dr/0/control/0", "/lpc/nct6796dr/0/control/3" ],
                "ControlScales": { "/lpc/nct6796dr/0/control/3": 0.6 }
            } ] } }
            """;
        var configuration = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        var options = new FanOMaxOptions();
        configuration.GetSection(FanOMaxOptions.Section).Bind(options);

        Assert.Equal(0.6, options.Groups[0].ControlScales[Fan4]);
        Assert.Empty(FanOMaxOptionsValidator.Validate(options));
    }

    [Theory]
    [InlineData(Fan3, 0.6, "n'est pas dans Controls")]
    [InlineData(Fan4, 0.1, "entre 0,3 et 2")]
    [InlineData(Fan4, 3.0, "entre 0,3 et 2")]
    public void Validate_RejectsInvalidScales(string control, double scale, string expected)
    {
        var options = Options();
        options.Groups[0].ControlScales = new(StringComparer.Ordinal) { [control] = scale };

        Assert.Contains(FanOMaxOptionsValidator.Validate(options), e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RequiresAReferenceOutputWithoutScale()
    {
        var options = Options();
        options.Groups[0].ControlScales = new(StringComparer.Ordinal) { [Fan1] = 0.8, [Fan2] = 0.8, [Fan4] = 0.6, [Fan7] = 1.2 };

        Assert.Contains(FanOMaxOptionsValidator.Validate(options), e => e.Contains("sans facteur", StringComparison.Ordinal));
    }
}
