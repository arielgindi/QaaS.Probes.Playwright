using Microsoft.Playwright;
using QaaS.Playwright.Browser;
using QaaS.Playwright.Recorder;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Panel;
using QaaS.Playwright.Recorder.TestRun;
using QaaS.Playwright.Tests.EndToEnd;
using static Microsoft.Playwright.Assertions;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>Testing the flow while recording: the code as it will be saved, built and run as the probe runs it.</summary>
[TestFixture]
[Category("EndToEnd")]
public class TestRunTests
{
    [SetUp]
    public void NeedsChrome()
    {
        if (ChromeExecutable.Find() is null) Assert.Ignore("Google Chrome is not installed.");
    }

    [Test]
    public async Task Test_InThePanel_PassesEveryStep_RecordsNothing_AndLeavesNothingLoaded()
    {
        using var site = TestSite.Start();
        var session = await StartAsync($"{site.Url}/helpdesk");
        try
        {
            await using var panel = await RecorderPanel.OpenAsync(session, RecordRequest.Parse(["record", "new-ticket", session.Options.Url]));
            var app = session.Page;
            var view = app.Context.Browser!.Contexts.Single(context => context != app.Context).Pages.Single();
            var code = view.GetByRole(AriaRole.Region, new() { Name = "Code" });
            await app.GetByTestId("new-ticket").ClickAsync();
            await Expect(code).ToContainTextAsync("ToBeVisibleAsync");
            await app.Locator("[name='title']").FillAsync("Printer jammed");
            await app.GetByRole(AriaRole.Button, new() { Name = "Create ticket" }).ClickAsync();
            await Expect(code).ToContainTextAsync("ToHaveURLAsync");
            var recorded = session.Build().Actions.Select(action => action.Id).ToList();

            await view.GetByRole(AriaRole.Button, new() { Name = "Test the flow" }).ClickAsync();
            await Expect(view.GetByRole(AriaRole.Status, new() { Name = "Test run" })).ToContainTextAsync("Passed", new() { Timeout = 60_000 });

            var report = panel.Tester.Report!;
            Assert.Multiple(() =>
            {
                Assert.That(report.Outcome, Is.EqualTo(TestOutcome.Passed), report.Failure?.Headline ?? report.Problem);
                Assert.That(report.Actions.Select(action => report.Steps[action].State), Has.All.EqualTo(StepState.Passed));
                Assert.That(site.Tickets, Is.Empty, "the ticket form is the app's own script");
                Assert.That(session.State, Is.EqualTo(RecordingState.Recording), "the recording goes on after the test");
                Assert.That(session.Build().Actions.Select(action => action.Id), Is.EqualTo(recorded),
                    "nothing the test did is recorded");
            });
            for (var collect = 0; collect < 20 && panel.Tester.LastBuild!.IsAlive; collect++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            Assert.That(panel.Tester.LastBuild!.IsAlive, Is.False, "the test's build is unloaded");
        }
        finally
        {
            await session.StopAsync();
        }
    }

    [Test]
    public async Task Test_FailsAtTheStepWhoseElementIsGone_WithWhatItWaitedFor_OnWhichPage()
    {
        using var site = TestSite.Start();
        var actions = 0;
        var session = await StartAsync($"{site.Url}/order", news => { if (news is CapturedAction) Interlocked.Increment(ref actions); });
        try
        {
            var app = session.Page;
            await app.GetByTestId("submit-order").ClickAsync();
            // A banner the app shows only once: the test run's page never has it.
            await app.EvaluateAsync("() => document.body.insertAdjacentHTML('beforeend', '<button id=\"dismiss-banner\">Dismiss</button>')");
            await app.Locator("#dismiss-banner").ClickAsync();
            await app.GetByTestId("submit-order").ClickAsync();
            for (var wait = 0; wait < 50 && Volatile.Read(ref actions) < 3; wait++) await Task.Delay(100);

            await using var tester = new FlowTester(session, new TestOptions { DefaultTimeout = 1000 });
            var request = RecordRequest.Parse(["record", "submit-order", session.Options.Url]);
            var report = await tester.RunAsync(request, "Flows");

            var flow = FlowCodeGenerator.Generate(session.Build(), request.ClassName, "Flows");
            var (passed, gone, after) = (report.Actions[0], report.Actions[1], report.Actions[2]);
            var failure = report.Failure!;
            Assert.Multiple(() =>
            {
                Assert.That(report.Outcome, Is.EqualTo(TestOutcome.Failed), report.Problem);
                Assert.That(failure.Action, Is.EqualTo(gone));
                Assert.That(failure.Line, Is.EqualTo(flow.ActionLines[gone].First));
                Assert.That(failure.Headline, Is.EqualTo(
                    $"Timeout 1000ms exceeded (waiting for Locator(\"[id='dismiss-banner']\")) on {site.Url}/order"));
                Assert.That(failure.Screenshot, Does.StartWith("data:image/png;base64,"));
                Assert.That(report.Steps[passed].State, Is.EqualTo(StepState.Passed));
                Assert.That(report.Steps[gone].State, Is.EqualTo(StepState.Failed));
                Assert.That(report.Steps[after].State, Is.EqualTo(StepState.NotRun));
                Assert.That(session.State, Is.EqualTo(RecordingState.Recording));
            });
        }
        finally
        {
            await session.StopAsync();
        }
    }

    // Never the person's own login.
    private static Task<RecordingSession> StartAsync(string url, Action<object>? onNews = null) =>
        RecordingSession.StartAsync(new RecordingOptions(url)
        {
            LoadStorageStatePath = null, SaveStorageStatePath = null, BrowserArgs = ["--headless=new"],
        }, onNews ?? (_ => { }));
}
