using System.Text;
using FanOMax.Core.Analysis;

namespace FanOMax.Core.Tests.Analysis;

public sealed class ShadowLogLookupTests : IDisposable
{
    private const string Header =
        "timestamp,group,mode,writing,target,temperature,regulator_temperature,filtered_temperature,power,filtered_power,feedforward,correction,decision_percent,status,applied_percent,written,reason";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "fanomax-lookup-" + Guid.NewGuid().ToString("N"));

    public ShadowLogLookupTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // Lignes réelles du 2026-10-10, juste avant l'arrêt brutal de 06:17.
    private static string Line(string time, string group = "CPU", string temperature = "63.63", string power = "72.78") =>
        $"{time},\"{group}\",Shadow,0,67,{temperature},64.78,66.85,{power},84.4,49.21,-5.6,43.79,Normal,45.75,0,";

    private void Write(string file, params string[] lines) =>
        File.WriteAllText(Path.Combine(_directory, file), Header + "\n" + string.Join("\n", lines) + "\n", new UTF8Encoding(false));

    [Fact]
    public void LastBefore_Restart_FindsTheLastMeasureBeforeTheMachineWentDown()
    {
        Write("shadow-20261010.csv",
            Line("2026-10-10T06:17:58.169", temperature: "64.38", power: "78.61"),
            Line("2026-10-10T06:17:59.173"),
            Line("2026-10-10T09:06:30.000", temperature: "40.1", power: "30"));

        var context = Assert.Single(ShadowLogLookup.LastBefore(_directory, [new(new DateTime(2026, 10, 10, 9, 6, 4), TimeSpan.FromDays(3))]));

        var cpu = Assert.Single(context);
        Assert.Equal(new DateTime(2026, 10, 10, 6, 17, 59, 173), cpu.Time);
        Assert.Equal(63.63, cpu.Temperature);
        Assert.Equal(72.78, cpu.Power);
    }

    [Fact]
    public void LastBefore_GivesOneLinePerGroup()
    {
        Write("shadow-20261010.csv",
            Line("2026-10-10T10:00:00.000", "CPU", "60"),
            Line("2026-10-10T10:00:00.100", "GPU", "55"),
            Line("2026-10-10T10:00:01.000", "CPU", "61"));

        var context = ShadowLogLookup.LastBefore(_directory, [new(new DateTime(2026, 10, 10, 10, 0, 2), TimeSpan.FromSeconds(10))])[0];

        Assert.Equal(["CPU", "GPU"], context.Select(r => r.Group));
        Assert.Equal(61, context[0].Temperature);
    }

    [Fact]
    public void LastBefore_TooOldMeasure_IsLeftOut()
    {
        Write("shadow-20261010.csv", Line("2026-10-10T10:00:00.000"));

        var context = ShadowLogLookup.LastBefore(_directory, [new(new DateTime(2026, 10, 10, 10, 5, 0), TimeSpan.FromSeconds(10))])[0];

        Assert.Empty(context);
    }

    [Fact]
    public void LastBefore_LooksIntoThePreviousDayAndLaterParts()
    {
        Write("shadow-20261009.csv", Line("2026-10-09T23:59:58.000", temperature: "50"));
        Write("shadow-20261009-2.csv", Line("2026-10-09T23:59:59.000", temperature: "51"));
        Write("shadow-20261010.csv", Line("2026-10-10T08:00:00.000", temperature: "40"));

        var contexts = ShadowLogLookup.LastBefore(_directory,
        [
            new(new DateTime(2026, 10, 10, 7, 0, 0), TimeSpan.FromDays(3)),
            new(new DateTime(2026, 10, 10, 8, 0, 5), TimeSpan.FromSeconds(10)),
        ]);

        Assert.Equal(51, Assert.Single(contexts[0]).Temperature);
        Assert.Equal(40, Assert.Single(contexts[1]).Temperature);
    }

    [Fact]
    public void LastBefore_MissingDirectory_GivesEmptyContexts()
    {
        var contexts = ShadowLogLookup.LastBefore(Path.Combine(_directory, "absent"), [new(DateTime.Now, TimeSpan.FromDays(1))]);

        Assert.Empty(Assert.Single(contexts));
    }
}
