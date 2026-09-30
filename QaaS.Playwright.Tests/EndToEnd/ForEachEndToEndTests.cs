using System.Diagnostics;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Playwright.Browser;
using AssertionOutcome = QaaS.Framework.SDK.Hooks.Assertion.AssertionStatus;

namespace QaaS.Playwright.Tests.EndToEnd;

/// <summary>ForEach: one flow run per DataSource item, spread over parallel workers.</summary>
[TestFixture]
[Category("EndToEnd")]
public class ForEachEndToEndTests
{
    private TestSite _site = null!;
    private HeadlessChrome _chrome = null!;

    [OneTimeSetUp]
    public async Task StartChromeAndSite()
    {
        _chrome = await HeadlessChrome.StartAsync(DesktopPointer.LaunchFlags);
        _site = TestSite.Start();
    }

    [OneTimeTearDown]
    public void StopChromeAndSite()
    {
        _chrome?.Dispose();
        _site?.Dispose();
    }

    [Test]
    public void TwelveItemsOnFourWorkers_AllPass_EachReported_OneLoginPerWorker()
    {
        var run = new QaasRun();
        string[] names = [.. Enumerable.Range(0, 12).Select(index => $"twelve-{index}")];
        var loginsBefore = _site.Logins;

        var assertion = run.RunAssertion(run.RunSession("Missions", Settings(parallelism: 4), Missions(names)));

        Assert.That(assertion.AssertionStatus, Is.EqualTo(AssertionOutcome.Passed), assertion.AssertionTrace);
        Assert.That(assertion.AssertionMessage, Does.StartWith("All 16 Playwright flow(s) passed"));
        Assert.That(assertion.AssertionMessage, Does.Contain("CreateMissionFlow[0]").And.Contains("CreateMissionFlow[11]"));
        Assert.That(_site.Missions.Where(names.Contains), Is.EquivalentTo(names), "each item created once");
        Assert.That(_site.Logins - loginsBefore, Is.EqualTo(4), "SetupFlows run once per worker");
    }

    [Test]
    public void ABadItem_FailsOnlyItself()
    {
        var run = new QaasRun();
        string[] names = ["good-0", "good-1", "bad", "good-3", "good-4", "good-5"];

        var session = run.RunSession("Missions", Settings(parallelism: 2), Missions(names));
        var assertion = run.RunAssertion(session);

        Assert.That(session.SessionFailures.Single().Reason.Message,
            Is.EqualTo("ForEach Missions: 1 of 6 items failed: 2."));
        Assert.That(assertion.AssertionStatus, Is.EqualTo(AssertionOutcome.Failed));
        Assert.That(assertion.AssertionMessage, Does.StartWith("CreateMissionFlow[2] failed (7/8 flows passed)"));
        Assert.That(_site.Missions, Is.SupersetOf(names.Where(name => name != "bad")), "the other items still ran");
    }

    [Test]
    public void FourWorkers_AreClearlyFasterThanOne()
    {
        string[] names = [.. Enumerable.Range(0, 8).Select(index => $"speed-{index}")];

        var oneWorker = TimeRun(Settings(parallelism: 1), names);
        var fourWorkers = TimeRun(Settings(parallelism: 4), names);

        Assert.That(oneWorker, Is.GreaterThan(TestSite.MissionCreationTime * names.Length), "one item after another");
        Assert.That(fourWorkers, Is.LessThan(oneWorker / 2), $"one worker took {oneWorker.TotalSeconds:0.0} s");
    }

    [Test]
    public void SetupFailingInEveryWorker_StopsTheRun()
    {
        var run = new QaasRun();
        var settings = Settings(parallelism: 2);
        settings["SetupFlows:0"] = "PlaceMissingOrderFlow";
        settings.Remove("FlowConfiguration:LogInFlow:User");
        settings["DefaultTimeout"] = "500";

        var session = run.RunSession("Missions", settings, Missions(["never-0", "never-1", "never-2"]));

        Assert.That(session.SessionFailures.Single().Reason.Message, Is.EqualTo(
            "ForEach Missions: 2 of 2 workers stopped, the first because: Timeout 500ms exceeded; 3 items did not run."));
        Assert.That(_site.Missions, Has.None.StartsWith("never"));
    }

    private TimeSpan TimeRun(Dictionary<string, string?> settings, string[] names)
    {
        var run = new QaasRun();
        var timer = Stopwatch.StartNew();
        var session = run.RunSession("Missions", settings, Missions(names));
        timer.Stop();

        Assert.That(session.SessionFailures, Is.Empty);
        return timer.Elapsed;
    }

    private static DataSource Missions(IEnumerable<string> names) =>
        ListGenerator.DataSource("Missions", [.. names.Select(name => $$"""{"name":"{{name}}"}""")]);

    private Dictionary<string, string?> Settings(int parallelism) => new()
    {
        ["BaseUrl"] = _site.Url,
        ["BrowserUrl"] = _chrome.Url,
        ["DefaultTimeout"] = "5000",
        ["SetupFlows:0"] = "LogInFlow",
        ["Flows:0"] = "CreateMissionFlow",
        ["FlowConfiguration:LogInFlow:User"] = "ops",
        ["ForEach"] = "Missions",
        ["Parallelism"] = parallelism.ToString(),
    };
}
