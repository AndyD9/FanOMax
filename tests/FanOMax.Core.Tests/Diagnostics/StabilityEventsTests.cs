using FanOMax.Core.Diagnostics;

namespace FanOMax.Core.Tests.Diagnostics;

public sealed class StabilityEventsTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0);

    private static SystemEventRecord Record(string provider, int id, int level, params (string Name, string Value)[] data) =>
        new(Now, provider, id, level, data.ToDictionary(d => d.Name, d => d.Value));

    [Fact]
    public void Classify_KernelPower41WithoutBugcheck_IsAnUnexpectedRestart()
    {
        // Champs réels du redémarrage du 2026-10-10 (sans écran bleu). Seul, sans l'échec du démarrage rapide qui l'accompagnait
        // (voir ClassifyAll), l'événement reste un redémarrage imprévu.
        var incident = StabilityEvents.Classify(Record(StabilityEvents.KernelPowerProvider, 41, 1, ("BugcheckCode", "0"), ("SleepInProgress", "6")));

        Assert.NotNull(incident);
        Assert.Equal(StabilityEventKind.UnexpectedRestart, incident.Kind);
        Assert.Equal(0, incident.BugcheckCode);
    }

    [Fact]
    public void Classify_KernelPower41WithBugcheck_KeepsTheCode()
    {
        var incident = StabilityEvents.Classify(Record(StabilityEvents.KernelPowerProvider, 41, 1, ("BugcheckCode", "292")));

        Assert.Equal(0x124, incident!.BugcheckCode);
    }

    [Fact]
    public void ClassifyAll_ShutdownEndedByFastStartupFailure_IsAFailedShutdownNotACrash()
    {
        // 2026-10-11 : PC éteint par l'utilisateur vers minuit ; au démarrage, Kernel-Boot 29 (0xC0000411) puis Kernel-Power 41 3 s après.
        var boot = new DateTime(2026, 10, 11, 10, 43, 38);
        var incidents = StabilityEvents.ClassifyAll(
        [
            KernelPower41(boot.AddSeconds(3), ("BugcheckCode", "0"), ("SleepInProgress", "6")),
            new SystemEventRecord(boot, StabilityEvents.KernelBootProvider, StabilityEvents.FastStartupFailedId, 2, new Dictionary<string, string>()),
        ]);

        var incident = Assert.Single(incidents);
        Assert.Equal(StabilityEventKind.FailedShutdown, incident.Kind);
    }

    [Fact]
    public void ClassifyAll_RestartWithoutFastStartupFailure_StaysAnUnexpectedRestart()
    {
        var incidents = StabilityEvents.ClassifyAll([KernelPower41(Now, ("BugcheckCode", "0"), ("SleepInProgress", "6"))]);

        Assert.Equal(StabilityEventKind.UnexpectedRestart, Assert.Single(incidents).Kind);
    }

    [Theory]
    [InlineData("0", "0")] // Pas d'arrêt en cours : plantage en marche.
    [InlineData("292", "6")] // Écran bleu : plantage, même pendant l'arrêt.
    public void ClassifyAll_CrashBesideAFastStartupFailure_StaysAnUnexpectedRestart(string bugcheck, string sleepInProgress)
    {
        var incidents = StabilityEvents.ClassifyAll(
        [
            KernelPower41(Now.AddSeconds(2), ("BugcheckCode", bugcheck), ("SleepInProgress", sleepInProgress)),
            new SystemEventRecord(Now, StabilityEvents.KernelBootProvider, StabilityEvents.FastStartupFailedId, 2, new Dictionary<string, string>()),
        ]);

        Assert.Equal(StabilityEventKind.UnexpectedRestart, Assert.Single(incidents).Kind);
    }

    [Fact]
    public void ClassifyAll_FastStartupFailureOfAnotherBoot_IsIgnored()
    {
        var incidents = StabilityEvents.ClassifyAll(
        [
            KernelPower41(Now, ("BugcheckCode", "0"), ("SleepInProgress", "6")),
            new SystemEventRecord(Now.AddHours(-1), StabilityEvents.KernelBootProvider, StabilityEvents.FastStartupFailedId, 2, new Dictionary<string, string>()),
        ]);

        Assert.Equal(StabilityEventKind.UnexpectedRestart, Assert.Single(incidents).Kind);
    }

    [Fact]
    public void Classify_CorrectedMachineCheck_GivesTheProcessorAndBank()
    {
        var incident = StabilityEvents.Classify(Record(StabilityEvents.WheaProvider, 19, 3, ("ApicId", "5"), ("MCABank", "0x5")));

        Assert.NotNull(incident);
        Assert.Equal(StabilityEventKind.CorrectedError, incident.Kind);
        Assert.Equal(5, incident.ApicId);
        Assert.Equal(5, incident.McaBank);
    }

    [Fact]
    public void Classify_FatalMachineCheck_IsFatal()
    {
        Assert.Equal(StabilityEventKind.FatalError, StabilityEvents.Classify(Record(StabilityEvents.WheaProvider, 18, 2, ("ApicId", "12")))!.Kind);
    }

    [Theory]
    [InlineData(17, 3, StabilityEventKind.CorrectedError)]
    [InlineData(47, 3, StabilityEventKind.CorrectedError)]
    [InlineData(1, 1, StabilityEventKind.FatalError)]
    [InlineData(20, 2, StabilityEventKind.FatalError)]
    public void Classify_OtherWheaEvents_FollowTheirIdOrLevel(int id, int level, StabilityEventKind expected)
    {
        Assert.Equal(expected, StabilityEvents.Classify(Record(StabilityEvents.WheaProvider, id, level))!.Kind);
    }

    [Fact]
    public void Classify_InformationalOrUnrelatedEvents_AreIgnored()
    {
        Assert.Null(StabilityEvents.Classify(Record(StabilityEvents.WheaProvider, 44, 4)));
        Assert.Null(StabilityEvents.Classify(Record(StabilityEvents.KernelPowerProvider, 42, 4)));
        Assert.Null(StabilityEvents.Classify(Record("EventLog", 6008, 2)));
    }

    [Fact]
    public void Classify_UnreadableNumber_LeavesTheFieldEmpty()
    {
        Assert.Null(StabilityEvents.Classify(Record(StabilityEvents.WheaProvider, 19, 3, ("ApicId", "n/a")))!.ApicId);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(5, 3)]
    [InlineData(15, 8)]
    public void CoreFromApicId_5800XWithSmt_GivesTheCoreNumberOfTheProcessorPanel(int apicId, int expected)
    {
        Assert.Equal(expected, StabilityEvents.CoreFromApicId(apicId, logicalProcessors: 16, cores: 8));
    }

    [Fact]
    public void CoreFromApicId_UnsureMapping_IsNull()
    {
        Assert.Equal(4, StabilityEvents.CoreFromApicId(3, logicalProcessors: 8, cores: 8));
        Assert.Null(StabilityEvents.CoreFromApicId(5, logicalProcessors: 16, cores: 0));
        Assert.Null(StabilityEvents.CoreFromApicId(5, logicalProcessors: 12, cores: 8));
        // 5900X : le second CCD commence à l'APIC 16, hors de la plage contiguë.
        Assert.Null(StabilityEvents.CoreFromApicId(27, logicalProcessors: 24, cores: 12));
    }

    [Fact]
    public void Query_FiltersWheaAndUnexpectedRestartsOverThePeriod()
    {
        var query = StabilityEvents.Query(30);

        Assert.Contains("Microsoft-Windows-WHEA-Logger", query, StringComparison.Ordinal);
        Assert.Contains("EventID=41", query, StringComparison.Ordinal);
        Assert.Contains("Microsoft-Windows-Kernel-Boot'] and EventID=29", query, StringComparison.Ordinal);
        Assert.Contains("<= 2592000000", query, StringComparison.Ordinal);
    }

    [Fact]
    public void Assess_NoIncident_IsStable()
    {
        var summary = StabilitySummary.Assess([], Now);

        Assert.Equal(StabilityLevel.Stable, summary.Level);
    }

    [Fact]
    public void Assess_RecentCorrectedErrors_AreAWarning()
    {
        var summary = StabilitySummary.Assess([Incident(StabilityEventKind.CorrectedError, days: 2), Incident(StabilityEventKind.CorrectedError, days: 3)], Now);

        Assert.Equal(StabilityLevel.Warning, summary.Level);
        Assert.Equal(2, summary.Corrected);
    }

    [Fact]
    public void Assess_RecentRestart_OutweighsCorrectedErrors()
    {
        var summary = StabilitySummary.Assess([Incident(StabilityEventKind.CorrectedError, days: 0), Incident(StabilityEventKind.UnexpectedRestart, days: 1)], Now);

        Assert.Equal(StabilityLevel.Alert, summary.Level);
        Assert.Equal(1, summary.Restarts);
    }

    [Fact]
    public void Assess_OldIncidents_AreCountedWithoutAlerting()
    {
        var summary = StabilitySummary.Assess([Incident(StabilityEventKind.UnexpectedRestart, days: 12)], Now);

        Assert.Equal(StabilityLevel.Stable, summary.Level);
        Assert.Equal(1, summary.Restarts);
    }

    [Fact]
    public void Assess_RecentFailedShutdowns_AreCountedWithoutAlerting()
    {
        var summary = StabilitySummary.Assess([Incident(StabilityEventKind.FailedShutdown, days: 0), Incident(StabilityEventKind.FailedShutdown, days: 1)], Now);

        Assert.Equal(StabilityLevel.Stable, summary.Level);
        Assert.Equal(0, summary.Restarts);
        Assert.Equal(2, summary.FailedShutdowns);
    }

    private static StabilityEvent Incident(StabilityEventKind kind, int days) => new(Now.AddDays(-days), kind, "", 0);

    private static SystemEventRecord KernelPower41(DateTime time, params (string Name, string Value)[] data) =>
        new(time, StabilityEvents.KernelPowerProvider, StabilityEvents.UnexpectedRestartId, 1, data.ToDictionary(d => d.Name, d => d.Value));
}
