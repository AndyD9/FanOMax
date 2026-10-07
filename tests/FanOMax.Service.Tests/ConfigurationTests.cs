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
