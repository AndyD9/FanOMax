using System.Text;
using FanOMax.Service.Configuration;
using Microsoft.Extensions.Configuration;
using static FanOMax.Service.Tests.FakeBackend;

namespace FanOMax.Service.Tests;

public class ConfigurationTests
{
    private static FanOMaxOptions Valid() => new()
    {
        Groups =
        [
            new GroupOptions { Name = "CPU", Kind = GroupKind.Cpu, Controls = [Fan1] },
            new GroupOptions { Name = "GPU", Kind = GroupKind.Gpu, Controls = [GpuFan] },
        ],
    };

    private static FanOMaxOptions Bind(string json)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
            .Build();
        var options = new FanOMaxOptions();
        configuration.GetSection(FanOMaxOptions.Section).Bind(options);
        return options;
    }

    [Fact]
    public void Validate_AcceptsAValidConfiguration()
    {
        Assert.Empty(FanOMaxOptionsValidator.Validate(Valid()));
    }

    [Fact]
    public void Validate_RejectsAnOutOfRangeInterval()
    {
        var options = Valid();
        options.IntervalSeconds = 0.1;

        Assert.Contains(FanOMaxOptionsValidator.Validate(options), e => e.Contains("IntervalSeconds", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsAControlSharedByTwoGroups()
    {
        var options = Valid();
        options.Groups[1].Controls.Add(Fan1);

        Assert.Contains(FanOMaxOptionsValidator.Validate(options), e => e.Contains(Fan1, StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsDuplicateNamesAndEmptyControls()
    {
        var options = Valid();
        options.Groups.Add(new GroupOptions { Name = "cpu", Kind = GroupKind.Cpu });

        var errors = FanOMaxOptionsValidator.Validate(options);

        Assert.Contains(errors, e => e.Contains("double", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("aucune sortie", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsATargetAboveTheCriticalTemperature()
    {
        var options = Valid();
        options.Groups[0].TargetTemperature = 92;

        Assert.NotEmpty(FanOMaxOptionsValidator.Validate(options));
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(20, true)]
    [InlineData(100, true)]
    [InlineData(120, false)]
    public void Validate_FixedPercentMustKeepTheFanSpinning(double percent, bool valid)
    {
        var options = Valid();
        options.Groups[0].FixedPercent = percent;

        Assert.Equal(valid, FanOMaxOptionsValidator.Validate(options).Count == 0);
    }

    [Fact]
    public void SofteningOptions_AreBound_Validated_AndApplied()
    {
        var options = Bind("""
            { "FanOMax": { "Mode": "Active", "Profile": "Silence", "Groups": [ {
                "Name": "CPU", "Kind": "Cpu", "Controls": [ "/lpc/nct6796dr/0/control/0" ], "TargetTemperature": 73,
                "MaxPercent": 70, "MaxRisePerSecond": 2, "PowerWindowSeconds": 60, "TargetDroop": 3, "Deadband": 3,
                "ProtectionCurve": [ { "Temperature": 77, "Percent": 40 }, { "Temperature": 82, "Percent": 70 } ]
            } ] } }
            """);

        Assert.Empty(FanOMaxOptionsValidator.Validate(options));

        var backend = new FakeBackend();
        var group = Engine.GroupRuntime.Resolve(options.Groups[0], options.Profile, backend, out var error);

        Assert.Null(error);
        Assert.Equal(70, group!.Settings.MaxPercent);
        Assert.Equal(3, group.Settings.TargetDroop);
        Assert.Equal(3, group.Settings.Deadband);
        Assert.Equal(2, group.Settings.MaxRisePerSecond);
        Assert.Equal(60, group.Settings.PowerWindowSeconds);
        Assert.Equal([(77d, 40d), (82d, 70d)], group.Settings.ProtectionCurve);

        // 76 °C à 118 W (cible intenable, sous le régime limité thermiquement) : la régulation s'arrête à 70 %.
        // Seule la température critique (90 °C) donne encore 100 %.
        backend.Set(CpuPower, 118);
        using var engine = new Engine.RegulationEngine(backend, options, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, () => false);
        var max = 0d;
        for (var i = 0; i < 200; i++)
        {
            backend.Set(CpuTemp, 76 + ((i % 3) * 0.125f));
            max = Math.Max(max, engine.Tick(1).Groups[0].Percent);
        }

        Assert.Equal(70, max, precision: 6);

        // La cible publiée (journal, interface) est la cible glissante : 73 °C + 3 °C au plafond.
        var tick = engine.Tick(1).Groups[0];
        Assert.Equal(76, tick.Target, precision: 6);

        backend.Set(CpuTemp, 91);
        Assert.Equal(100, engine.Tick(1).Groups[0].Percent);
    }

    [Fact]
    public void SofteningOptions_DefaultToTheProfile()
    {
        var group = Engine.GroupRuntime.Resolve(Valid().Groups[0], Core.Control.FanProfile.Silence, new FakeBackend(), out _);

        Assert.Equal(100, group!.Settings.MaxPercent);
        Assert.Equal(5, group.Settings.MaxRisePerSecond);
        Assert.Equal(25, group.Settings.PowerWindowSeconds);
        Assert.Equal(Core.Control.RegulatorSettings.CpuProtectionCurve, group.Settings.ProtectionCurve);
    }

    [Theory]
    [InlineData(30, 2, 60, false)]
    [InlineData(70, 0.1, 60, false)]
    [InlineData(70, 2, 300, false)]
    [InlineData(70, 2, 60, true)]
    public void Validate_SofteningOptionsMustStayInRange(double maxPercent, double rise, double window, bool valid)
    {
        var options = Valid();
        options.Groups[0].MaxPercent = maxPercent;
        options.Groups[0].MaxRisePerSecond = rise;
        options.Groups[0].PowerWindowSeconds = window;

        Assert.Equal(valid, FanOMaxOptionsValidator.Validate(options).Count == 0);
    }

    [Fact]
    public void Validate_RejectsAProtectionCurveThatGoesDown()
    {
        var options = Valid();
        options.Groups[0].ProtectionCurve = [new() { Temperature = 80, Percent = 60 }, new() { Temperature = 78, Percent = 70 }];

        Assert.Contains(FanOMaxOptionsValidator.Validate(options), e => e.Contains("croissante", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_IgnoresDisabledGroups()
    {
        var options = Valid();
        options.Groups.Add(new GroupOptions { Name = "Désactivé", Enabled = false });

        Assert.Empty(FanOMaxOptionsValidator.Validate(options));
    }

    [Fact]
    public void DefaultConfig_IsValidCommentedJson_WithTheDetectedHardware()
    {
        var json = DefaultConfig.Build(new FakeBackend());

        var options = Bind(json);

        Assert.Equal(OperatingMode.Shadow, options.Mode);
        Assert.Equal(Core.Control.FanProfile.Normal, options.Profile);
        Assert.Empty(FanOMaxOptionsValidator.Validate(options));

        var cpu = Assert.Single(options.Groups, g => g.Name == "CPU");
        Assert.Equal(CpuTemp, cpu.TemperatureSensor);
        Assert.Equal(CpuPower, cpu.PowerSensor);

        // Fan #3 ne tourne pas (canal vide) : il n'est pas piloté.
        Assert.Equal([Fan1, Fan2, Fan7], cpu.Controls);

        var gpu = Assert.Single(options.Groups, g => g.Name == "GPU");
        Assert.Equal(GpuHotSpot, gpu.TemperatureSensor);
        Assert.Equal([GpuFan], gpu.Controls);
    }

    /// <summary>Fichiers de configuration fournis pour la bascule (docs/phase7) : tous valides et résolubles.</summary>
    [Fact]
    public void Phase7ConfigFiles_AreValid_AndMatchTheHardware()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "FanOMax.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var files = Directory.GetFiles(Path.Combine(root.FullName, "docs", "phase7"), "*.json");
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var options = Bind(File.ReadAllText(file));
            Assert.True(FanOMaxOptionsValidator.Validate(options).Count == 0, $"{Path.GetFileName(file)} : {string.Join(" ; ", FanOMaxOptionsValidator.Validate(options))}");

            using var engine = new Engine.RegulationEngine(new FakeBackend(), options, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, () => false);
            Assert.True(engine.ConfigurationErrors.Count == 0, $"{Path.GetFileName(file)} : {string.Join(" ; ", engine.ConfigurationErrors)}");
            Assert.Equal(options.Groups.Count(g => g.Enabled), engine.GroupNames.Count);
        }
    }

    [Fact]
    public void DefaultConfig_IsWrittenOnlyOnce()
    {
        var directory = Directory.CreateTempSubdirectory("fanomax-");
        try
        {
            var path = Path.Combine(directory.FullName, "config.json");

            Assert.True(DefaultConfig.WriteIfMissing(path, new FakeBackend()));
            File.WriteAllText(path, "{ \"FanOMax\": { \"Mode\": \"Active\" } }");
            Assert.False(DefaultConfig.WriteIfMissing(path, new FakeBackend()));
            Assert.Contains("Active", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
