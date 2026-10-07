using FanOMax.Core.Sensors;

namespace FanOMax.Core.Tests.Sensors;

public class KeySensorResolverTests
{
    // Extrait représentatif des capteurs exposés par LHM sur un Ryzen 5000 + Radeon RX 6000 + Nuvoton.
    private static readonly List<(string Id, string Name)> Sensors =
    [
        ("/amdcpu/0/load/0", "CPU Total"),
        ("/amdcpu/0/load/1", "CPU Core Max"),
        ("/amdcpu/0/temperature/2", "Core (Tctl/Tdie)"),
        ("/amdcpu/0/temperature/3", "CCD1 (Tdie)"),
        ("/amdcpu/0/power/0", "Package"),
        ("/amdcpu/0/power/1", "Core #1"),
        ("/gpu-amd/0/temperature/0", "GPU Core"),
        ("/gpu-amd/0/temperature/2", "GPU Hot Spot"),
        ("/gpu-amd/0/power/0", "GPU Core"),
        ("/gpu-amd/0/power/3", "GPU Package"),
        ("/gpu-amd/0/load/0", "GPU Core"),
        ("/lpc/nct6779d/0/temperature/0", "CPU Core"),
        ("/lpc/nct6779d/0/fan/1", "Fan #2"),
        ("/lpc/nct6779d/0/control/1", "Fan #2"),
    ];

    [Theory]
    [InlineData(KeySensor.CpuTemperature, "/amdcpu/0/temperature/2")]
    [InlineData(KeySensor.CpuPower, "/amdcpu/0/power/0")]
    [InlineData(KeySensor.CpuLoad, "/amdcpu/0/load/0")]
    [InlineData(KeySensor.GpuTemperature, "/gpu-amd/0/temperature/0")]
    [InlineData(KeySensor.GpuHotSpot, "/gpu-amd/0/temperature/2")]
    [InlineData(KeySensor.GpuPower, "/gpu-amd/0/power/3")]
    [InlineData(KeySensor.GpuLoad, "/gpu-amd/0/load/0")]
    public void Find_PicksTheExpectedSensor(KeySensor key, string expectedId)
    {
        var index = KeySensorResolver.Find(Sensors, key);

        Assert.True(index >= 0);
        Assert.Equal(expectedId, Sensors[index].Id);
    }

    [Fact]
    public void Find_IgnoresMotherboardSensorsForCpuTemperature()
    {
        // La sonde « CPU Core » de la carte mère ne doit pas être confondue avec Tctl.
        var onlyMotherboard = Sensors.Where(s => s.Id.StartsWith("/lpc/", StringComparison.Ordinal)).ToList();

        Assert.Equal(-1, KeySensorResolver.Find(onlyMotherboard, KeySensor.CpuTemperature));
    }

    [Fact]
    public void Find_ReturnsMinusOne_WhenHotSpotIsMissing()
    {
        var withoutHotSpot = Sensors.Where(s => s.Name != "GPU Hot Spot").ToList();

        Assert.Equal(-1, KeySensorResolver.Find(withoutHotSpot, KeySensor.GpuHotSpot));
    }

    [Theory]
    [InlineData("/amdcpu/0/temperature/2", SensorKind.Temperature)]
    [InlineData("/lpc/nct6779d/0/fan/1", SensorKind.Fan)]
    [InlineData("/lpc/nct6779d/0/control/1", SensorKind.Control)]
    [InlineData("/gpu-amd/0/power/3", SensorKind.Power)]
    [InlineData("/amdcpu/0/smalldata/0", SensorKind.Other)]
    [InlineData("garbage", SensorKind.Other)]
    public void KindFromId_ReadsTheSensorTypeSegment(string id, SensorKind expected)
    {
        Assert.Equal(expected, SensorDescriptor.KindFromId(id));
    }
}
