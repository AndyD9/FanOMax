using System.Text;
using FanOMax.Core.Analysis;

namespace FanOMax.Core.Tests.Analysis;

public sealed class ShadowLogTailTests : IDisposable
{
    // En-tête réel du service (ShadowLog.Header) et une ligne réelle du 2026-10-09.
    private const string Header =
        "timestamp,group,mode,writing,target,temperature,regulator_temperature,filtered_temperature,power,filtered_power,feedforward,correction,decision_percent,status,applied_percent,written,reason";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "fanomax-tail-" + Guid.NewGuid().ToString("N"));

    public ShadowLogTailTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static string Line(string time, double temperature = 51.38, string status = "Normal", string reason = "") =>
        $"2026-10-09T{time},\"CPU\",Active,1,69,{temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)},51.38,50.62,41.24,39.65,25,0,25.5,{status},25.49,1,{reason}";

    private void Append(string file, string text) => File.AppendAllText(Path.Combine(_directory, file), text, new UTF8Encoding(false));

    private ShadowLogTail NewTail() => new(_directory, new DateOnly(2026, 10, 9));

    [Fact]
    public void ReadNew_ParsesRealLineAndReturnsOnlyNewLines()
    {
        Append("shadow-20261009.csv", Header + "\n" + Line("10:09:55.026") + "\n");
        var tail = NewTail();

        var first = Assert.Single(tail.ReadNew());
        Assert.Equal(new DateTime(2026, 10, 9, 10, 9, 55, 26), first.Time);
        Assert.Equal("CPU", first.Group);
        Assert.Equal("Active", first.Mode);
        Assert.True(first.Writing);
        Assert.Equal(69, first.Target);
        Assert.Equal(51.38, first.Temperature);
        Assert.Equal(41.24, first.Power);
        Assert.Equal(25.5, first.Decision);
        Assert.Equal(25.49, first.Applied);
        Assert.Equal("Normal", first.Status);

        Assert.Empty(tail.ReadNew());

        Append("shadow-20261009.csv", Line("10:09:56.028", 50.63) + "\n");
        Assert.Equal(50.63, Assert.Single(tail.ReadNew()).Temperature);
    }

    [Fact]
    public void ReadNew_WaitsForTheEndOfALineBeingWritten()
    {
        var line = Line("10:09:55.026", reason: "\"Température invalide\"");
        Append("shadow-20261009.csv", Header + "\n" + line[..40]);
        var tail = NewTail();

        Assert.Empty(tail.ReadNew());

        Append("shadow-20261009.csv", line[40..] + "\n");
        Assert.Equal("Température invalide", Assert.Single(tail.ReadNew()).Reason);
    }

    [Fact]
    public void ReadNew_FollowsNextFilesInOrder()
    {
        Append("shadow-20261008.csv", Header + "\n" + Line("00:00:01.000") + "\n");
        Append("shadow-20261009.csv", Header + "\n" + Line("10:00:00.000", 40) + "\n");
        Append("shadow-20261009-2.csv", Header + "\n" + Line("11:00:00.000", 41) + "\n");
        var tail = NewTail();

        // Le jour précédent le premier jour est ignoré ; -2 vient après le fichier principal du jour.
        Assert.Equal([40.0, 41.0], tail.ReadNew().Select(r => r.Temperature!.Value));

        Append("shadow-20261009-10.csv", Header + "\n" + Line("12:00:00.000", 43) + "\n");
        Append("shadow-20261010.csv", Header + "\n" + Line("00:00:01.000", 44) + "\n");
        Append("shadow-20261009-2.csv", Line("11:00:01.000", 42) + "\n");

        Assert.Equal([42.0, 43.0, 44.0], tail.ReadNew().Select(r => r.Temperature!.Value));
    }

    [Fact]
    public void ReadNew_ReadsOlderFormatsWithMissingColumns()
    {
        Append("shadow-20261009.csv", "timestamp,group,target,temperature,decision_percent,status,applied_percent\n2026-10-09T08:00:00.000,\"GPU\",82,60.5,40,Normal,55\n");

        var record = Assert.Single(NewTail().ReadNew());

        Assert.Equal("GPU", record.Group);
        Assert.Equal(60.5, record.Temperature);
        Assert.Null(record.RegulatorTemperature);
        Assert.Null(record.Power);
        Assert.Equal(55, record.Applied);
    }

    [Fact]
    public void ReadNew_MissingDirectory_ReturnsNothing()
    {
        Assert.Empty(new ShadowLogTail(Path.Combine(_directory, "absent"), new DateOnly(2026, 10, 9)).ReadNew());
    }
}
