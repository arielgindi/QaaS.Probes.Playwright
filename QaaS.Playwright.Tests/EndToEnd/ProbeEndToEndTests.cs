using QaaS.Playwright.Browser;
using AssertionOutcome = QaaS.Framework.SDK.Hooks.Assertion.AssertionStatus;

namespace QaaS.Playwright.Tests.EndToEnd;

/// <summary>The real probe and assertion against a real headless Chrome and a local web app.</summary>
[TestFixture]
[Category("EndToEnd")]
public class ProbeEndToEndTests
{
    private TestSite _site = null!;
    private HeadlessChrome _chrome = null!;
    private string _tempDir = null!;

    [OneTimeSetUp]
    public async Task StartChromeAndSite()
    {
        _chrome = await HeadlessChrome.StartAsync(DesktopPointer.LaunchFlags);
        _site = TestSite.Start();
        _tempDir = Directory.CreateTempSubdirectory("qaas-e2e-").FullName;
    }

    [OneTimeTearDown]
    public void StopChromeAndSite()
    {
        _chrome?.Dispose();
        _site?.Dispose();
        if (_tempDir is not null) Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public void PassingFlows_PassTheAssertion_AndAreNamed()
    {
        var run = new QaasRun();
        var settings = Settings("CheckUserFlow", "SubmitOrderFlow");
        settings["SetupFlows:0"] = "LogInFlow";
        settings["FlowConfiguration:LogInFlow:User"] = "alice";
        settings["FlowConfiguration:CheckUserFlow:User"] = "alice";

        var assertion = run.RunAssertion(run.RunSession("Journey", settings));

        Assert.That(assertion.AssertionStatus, Is.EqualTo(AssertionOutcome.Passed), assertion.AssertionTrace);
        Assert.That(assertion.AssertionMessage,
            Is.EqualTo("All 3 Playwright flow(s) passed: LogInFlow, CheckUserFlow, SubmitOrderFlow."));
    }

    [Test]
    public void MisspelledFlowsKey_FailsTheSessionBeforeTheBrowserOpens()
    {
        var run = new QaasRun();
        var settings = Settings();
        settings["Flow:0"] = "SubmitOrderFlow";

        var session = run.RunSession("Typo", settings);
        var assertion = run.RunAssertion(session);

        Assert.That(session.SessionFailures.Single().Reason.Message, Is.EqualTo(
            "ProbeConfiguration has 2 problem(s):\n" +
            "  - Flow: not a setting (known: BaseUrl, SetupFlows, Flows, ForEach, Parallelism, Headless, BlockAssets, " +
            "DefaultTimeout, ViewportWidth, ViewportHeight, FullPageScreenshot, SlowMo, KeepOpen, SaveStorageStatePath, " +
            "LoadStorageStatePath, IsolateContext, EmulateDesktopPointer, BrowserUrl, BrowserExecutablePath, " +
            "FlowConfiguration)\n" +
            "  - Flows: nothing to run: Flows and SetupFlows are both empty"));
        Assert.That(run.Log.Messages, Has.None.StartsWith("Navigating to"));
        Assert.That(assertion.AssertionStatus, Is.EqualTo(AssertionOutcome.Failed));
        Assert.That(assertion.AssertionMessage, Does.StartWith("1 session failure(s): ProbeConfiguration has 2 problem(s)"));
    }

    [Test]
    public void FailingFlow_HeadlineNamesTheElementAndThePage()
    {
        var run = new QaasRun();
        var settings = Settings("SubmitOrderFlow", "PlaceMissingOrderFlow");
        settings["DefaultTimeout"] = "1000";

        var assertion = run.RunAssertion(run.RunSession("Failing", settings));

        Assert.That(assertion.AssertionStatus, Is.EqualTo(AssertionOutcome.Failed));
        Assert.That(assertion.AssertionMessage, Is.EqualTo(
            "PlaceMissingOrderFlow failed (1/2 flows passed): Timeout 1000ms exceeded " +
            $"(waiting for GetByRole(AriaRole.Button, new() {{ Name = \"Place order\" }})) on {_site.Url}/order. " +
            "Passed: SubmitOrderFlow."));
        Assert.That(assertion.AssertionAttachments, Has.Count.EqualTo(1), "the failure screenshot");
    }

    [Test]
    public void PasswordAFlowFills_NeverShowsInTheSessionFailureTheReportOrTheLog()
    {
        var run = new QaasRun();
        var settings = Settings("FillReadOnlyPasswordFlow");
        settings["DefaultTimeout"] = "500";
        settings["FlowConfiguration:FillReadOnlyPasswordFlow:Password"] = "hunter2-secret";

        var session = run.RunSession("Secret", settings);
        var assertion = run.RunAssertion(session);

        var failure = session.SessionFailures.Single().Reason.Message;
        Assert.That(failure, Does.StartWith("TimeoutException: Timeout 500ms exceeded."));
        Assert.That(assertion.AssertionTrace, Does.Contain("fill(\"***\")"));
        string?[] everything = [failure, assertion.AssertionMessage, assertion.AssertionTrace, .. run.Log.Messages];
        Assert.That(string.Join('\n', everything), Does.Not.Contain("hunter2-secret"));
    }

    [Test]
    public void SavedStorageState_LetsALaterRunSkipTheLogin()
    {
        var run = new QaasRun();
        var statePath = Path.Combine(_tempDir, "carol.json");
        var login = Settings("LogInFlow");
        login["FlowConfiguration:LogInFlow:User"] = "carol";
        login["SaveStorageStatePath"] = statePath;
        var reuse = Settings("CheckUserFlow");
        reuse["FlowConfiguration:CheckUserFlow:User"] = "carol";
        reuse["LoadStorageStatePath"] = statePath;

        var loggedIn = run.RunSession("Login", login);
        var reused = run.RunSession("Reuse", reuse);

        var assertion = run.RunAssertion(loggedIn, reused);
        Assert.That(assertion.AssertionStatus, Is.EqualTo(AssertionOutcome.Passed), assertion.AssertionTrace);
    }

    private Dictionary<string, string?> Settings(params string[] flows)
    {
        var settings = new Dictionary<string, string?>
        {
            ["BaseUrl"] = _site.Url,
            ["BrowserUrl"] = _chrome.Url,
            ["DefaultTimeout"] = "5000",
        };
        for (var index = 0; index < flows.Length; index++) settings[$"Flows:{index}"] = flows[index];
        return settings;
    }
}
