using System.Diagnostics;
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
    private HeadlessChrome _chromeWithoutMouse = null!;
    private string _tempDir = null!;

    [OneTimeSetUp]
    public async Task StartChromeAndSite()
    {
        // Started with the flags the docs recommend, so it reports a mouse like a desktop browser.
        _chrome = await HeadlessChrome.StartAsync(DesktopPointer.LaunchFlags);
        _chromeWithoutMouse = await HeadlessChrome.StartAsync();
        _site = TestSite.Start();
        _tempDir = Directory.CreateTempSubdirectory("qaas-e2e-").FullName;
    }

    [OneTimeTearDown]
    public void StopChromeAndSite()
    {
        _chrome?.Dispose();
        _chromeWithoutMouse?.Dispose();
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
    public void MisspelledFlowsKey_IsWarnedAbout_AndFailsTheAssertion()
    {
        var run = new QaasRun();
        var settings = Settings();
        settings["Flow:0"] = "SubmitOrderFlow";

        var assertion = run.RunAssertion(run.RunSession("Typo", settings));

        Assert.That(run.Log.Warnings, Has.One.Contains("Unknown ProbeConfiguration key 'Flow'"));
        Assert.That(assertion.AssertionStatus, Is.EqualTo(AssertionOutcome.Failed));
        Assert.That(assertion.AssertionMessage, Does.Contain("session(s) Typo").And.Contains("nothing was verified"));
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
    public void GetByTestId_ResolvesDataTestId()
    {
        var run = new QaasRun();

        var assertion = run.RunAssertion(run.RunSession("TestId", Settings("SubmitOrderFlow")));

        Assert.That(assertion.AssertionStatus, Is.EqualTo(AssertionOutcome.Passed), assertion.AssertionTrace);
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

    [Test]
    [Repeat(3)]
    public async Task ParallelSessionsAsDifferentUsers_NeverMixUp()
    {
        string[] users = ["ann", "ben", "cat", "dan"];
        var run = new QaasRun();
        LogInFlow.AllLoggedIn = new Barrier(users.Length);
        try
        {
            var sessions = await run.RunSessionsInParallelAsync(users.Select(user => (user, LogInAndCheck(user))));

            var assertion = run.RunAssertion(sessions);
            Assert.That(assertion.AssertionStatus, Is.EqualTo(AssertionOutcome.Passed), assertion.AssertionTrace);
        }
        finally
        {
            LogInFlow.AllLoggedIn = null;
        }

        Dictionary<string, string?> LogInAndCheck(string user)
        {
            var settings = Settings("LogInFlow", "CheckUserFlow");
            settings["FlowConfiguration:LogInFlow:User"] = user;
            settings["FlowConfiguration:CheckUserFlow:User"] = user;
            return settings;
        }
    }

    [Test]
    public void BrowserWithoutMouse_IsWarnedAbout()
    {
        // The warning is logged once per process, so this must stay the only test that runs on the Chrome without a
        // mouse and does not emulate one.
        var run = new QaasRun();
        var settings = Settings("CheckPointerFlow");
        settings["BrowserUrl"] = _chromeWithoutMouse.Url;
        settings["FlowConfiguration:CheckPointerFlow:Expected"] = "none";

        var assertion = run.RunAssertion(run.RunSession("NoMouse", settings));

        Assert.That(assertion.AssertionStatus, Is.EqualTo(AssertionOutcome.Passed), assertion.AssertionTrace);
        Assert.That(run.Log.Warnings, Has.One.Contains("reports no mouse").And.Contains(DesktopPointer.LaunchFlags));
    }

    [Test]
    public void EmulateDesktopPointer_MakesPointerFineMatch()
    {
        var run = new QaasRun();
        var settings = Settings("CheckPointerFlow");
        settings["BrowserUrl"] = _chromeWithoutMouse.Url;
        settings["EmulateDesktopPointer"] = "true";
        settings["FlowConfiguration:CheckPointerFlow:Expected"] = "fine";

        var assertion = run.RunAssertion(run.RunSession("EmulatedMouse", settings));

        Assert.That(assertion.AssertionStatus, Is.EqualTo(AssertionOutcome.Passed), assertion.AssertionTrace);
    }

    [Test]
    public void ChromeStartedWithTheLaunchFlags_ReportsAMouse()
    {
        var run = new QaasRun();
        var settings = Settings("CheckPointerFlow");
        settings["FlowConfiguration:CheckPointerFlow:Expected"] = "fine";

        var assertion = run.RunAssertion(run.RunSession("DesktopFlags", settings));

        Assert.That(assertion.AssertionStatus, Is.EqualTo(AssertionOutcome.Passed), assertion.AssertionTrace);
    }

    [Test]
    public void IsolateContextFalse_SharesTheBrowsersDefaultContext()
    {
        var run = new QaasRun();
        var logIn = Settings("LogInFlow");
        logIn["IsolateContext"] = "false";
        logIn["FlowConfiguration:LogInFlow:User"] = "eve";
        var shared = Settings("CheckUserFlow");
        shared["IsolateContext"] = "false";
        shared["FlowConfiguration:CheckUserFlow:User"] = "eve";
        var isolated = Settings("CheckUserFlow");
        isolated["FlowConfiguration:CheckUserFlow:User"] = "nobody";

        var sessions = new[]
        {
            run.RunSession("LogIn", logIn), run.RunSession("Shared", shared), run.RunSession("Isolated", isolated),
        };

        var assertion = run.RunAssertion(sessions);
        Assert.That(assertion.AssertionStatus, Is.EqualTo(AssertionOutcome.Passed), assertion.AssertionTrace);
    }

    [Test]
    public void BlockAssets_BlocksImages_AndKeepsTheHttpCache()
    {
        var run = new QaasRun();
        var blocked = Settings("CheckImageFlow", "SubmitOrderFlow");
        blocked["FlowConfiguration:CheckImageFlow:Expected"] = "blocked";
        var loaded = Settings("CheckImageFlow");
        loaded["BlockAssets"] = "false";
        loaded["FlowConfiguration:CheckImageFlow:Expected"] = "loaded";
        var downloadsBefore = _site.ScriptDownloads;

        var assertion = run.RunAssertion(run.RunSession("Blocked", blocked), run.RunSession("Loaded", loaded));

        Assert.That(assertion.AssertionStatus, Is.EqualTo(AssertionOutcome.Passed), assertion.AssertionTrace);
        Assert.That(_site.ScriptDownloads - downloadsBefore, Is.EqualTo(2), "one download per run, not per page load");
    }

    [Test]
    public async Task TenProbesInParallel_AllPass()
    {
        var run = new QaasRun();

        var sessions = await run.RunSessionsInParallelAsync(
            Enumerable.Range(1, 10).Select(index => ($"Session{index}", Settings("SubmitOrderFlow"))));

        var assertion = run.RunAssertion(sessions);
        Assert.That(assertion.AssertionStatus, Is.EqualTo(AssertionOutcome.Passed), assertion.AssertionTrace);
        Assert.That(assertion.AssertionMessage, Does.StartWith("All 10 Playwright flow(s) passed"));
        Assert.That(run.Log.Messages.Count(message => message.StartsWith("Connecting to")), Is.AtMost(1),
            "the runs share one connection");
    }

    [Test]
    public async Task ProbesOfOneSession_FailingTheSameFlow_AttachDistinctScreenshots()
    {
        var run = new QaasRun();
        var failing = Settings("PlaceMissingOrderFlow");
        failing["DefaultTimeout"] = "1000";

        var session = await run.RunSessionAsync("Orders",
            ("Submit", Settings("SubmitOrderFlow")), ("PlaceFirst", failing), ("PlaceSecond", failing));

        var assertion = run.RunAssertion(session);
        Assert.That(assertion.AssertionStatus, Is.EqualTo(AssertionOutcome.Failed));
        var paths = assertion.AssertionAttachments.Select(attachment => attachment.Path).ToList();
        Assert.That(paths, Has.Count.EqualTo(2).And.Unique.IgnoreCase);
        Assert.That(paths, Has.One.Contains("PlaceFirst").And.One.Contains("PlaceSecond"));
    }

    [Test]
    public async Task FiveProbesOfOneSession_RunInAboutTheTimeOfOne()
    {
        var run = new QaasRun();
        var timer = Stopwatch.StartNew();
        run.RunSession("One", Settings("AnimationFlow"));
        var one = timer.Elapsed;

        timer.Restart();
        var session = await run.RunSessionAsync("Five",
            [.. Enumerable.Range(1, 5).Select(index => ($"Probe{index}", Settings("AnimationFlow")))]);
        var five = timer.Elapsed;

        var assertion = run.RunAssertion(session);
        Assert.That(assertion.AssertionStatus, Is.EqualTo(AssertionOutcome.Passed), assertion.AssertionTrace);
        Assert.That(five, Is.LessThan(one * 3), $"one probe took {one.TotalMilliseconds:0} ms");
    }

    [Test]
    public async Task ChromeRestarted_TheNextRunReconnects()
    {
        var chrome = await HeadlessChrome.StartAsync(DesktopPointer.LaunchFlags);
        try
        {
            var settings = Settings("SubmitOrderFlow");
            settings["BrowserUrl"] = chrome.Url;
            var before = new QaasRun();
            before.RunSession("BeforeRestart", settings);

            chrome = await chrome.RestartAsync();
            var after = new QaasRun();
            var assertion = after.RunAssertion(after.RunSession("AfterRestart", settings));

            Assert.That(assertion.AssertionStatus, Is.EqualTo(AssertionOutcome.Passed), assertion.AssertionTrace);
            Assert.That(after.Log.Messages, Has.One.StartsWith("Connecting to"));
        }
        finally
        {
            chrome.Dispose();
        }
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
