using System.Text.RegularExpressions;
using Microsoft.Playwright;
using QaaS.Playwright.Browser;
using QaaS.Playwright.Recorder;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Panel;
using QaaS.Playwright.Recorder.TestRun;
using QaaS.Playwright.Tests.EndToEnd;
using QaaS.Playwright.Recorder.State;
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
            await Expect(code).ToContainTextAsync("ToBeHiddenAsync");
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
    public async Task Test_FailsAtTheRepeatWhoseElementIsGone_WithWhatItWaitedFor_OnWhichPage()
    {
        using var site = TestSite.Start();
        var actions = 0;
        var session = await StartAsync($"{site.Url}/todos", news => { if (news is CapturedAction) Interlocked.Increment(ref actions); });
        try
        {
            // Three tasks typed alike make a loop. The list takes three here, and two in the test, as on a new page.
            var app = session.Page;
            await app.EvaluateAsync("() => window.limit = 3");
            foreach (var task in (string[])["Buy milk", "Pay the bill", "Return the books"])
            {
                await app.Locator("#new-task").FillAsync(task);
                await app.Locator("#new-task").PressAsync("Enter");
            }

            for (var wait = 0; wait < 50 && Volatile.Read(ref actions) < 6; wait++) await Task.Delay(100);

            await using var tester = new FlowTester(session, new TestOptions { DefaultTimeout = 1000 });
            var request = RecordRequest.Parse(["record", "add-tasks", session.Options.Url]);
            var report = await tester.RunAsync(request, "Flows");

            var flow = FlowCodeGenerator.Generate(session.Build(), request.ClassName, "Flows");
            var gone = report.Actions[4];
            var failure = report.Failure!;
            Assert.Multiple(() =>
            {
                Assert.That(report.Code, Does.Contain("foreach (var task in Configuration.NewTasks)"), "the repeats are a loop");
                Assert.That(report.Outcome, Is.EqualTo(TestOutcome.Failed), report.Problem);
                Assert.That(failure.Action, Is.EqualTo(gone), "the third repeat's fill");
                Assert.That(failure.Line, Is.EqualTo(flow.ActionLines[gone].First), "the loop's line that fills");
                Assert.That(failure.Headline, Is.EqualTo(
                    $"Timeout 1000ms exceeded (waiting for Locator(\"[id='new-task']\")) on {site.Url}/todos"));
                Assert.That(failure.Screenshot, Does.StartWith("data:image/png;base64,"));
                Assert.That(report.Actions.Select(action => report.Steps[action].State), Is.EqualTo(new[]
                {
                    StepState.Passed, StepState.Passed, StepState.Passed, StepState.Passed, StepState.Failed, StepState.NotRun,
                }));
                Assert.That(session.State, Is.EqualTo(RecordingState.Recording));
            });
        }
        finally
        {
            await session.StopAsync();
        }
    }

    [Test]
    public async Task ChecksChosenInTheApp_AndEditsInThePanel_AreInTheCode_AndTheTestPassesWithThem()
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
            await Expect(code).ToContainTextAsync("ToBeHiddenAsync");

            // F6, then a click picks what to check, and Enter adds the check the chooser offers first.
            await app.Keyboard.PressAsync("F6");
            await app.GetByText("Ticket TCK-1043 created").ClickAsync();
            await app.Keyboard.PressAsync("Enter");
            await Expect(code).ToContainTextAsync("""ToContainTextAsync("created"); // check""");
            await view.GetByRole(AriaRole.Button, new() { Name = "Check something in the app" }).ClickAsync();
            // In a row, the click offers the button, its row and its table: Enter picks the button, and Enter adds its check.
            await app.GetByRole(AriaRole.Row, new() { Name = "TCK-1042" }).GetByRole(AriaRole.Button).ClickAsync();
            await app.Keyboard.PressAsync("Enter");
            await app.Keyboard.PressAsync("Enter");
            // Found by the text it is checked to have, it is checked to show: a check of its text would pass while it is there.
            await Expect(code).ToContainTextAsync("""Name = "Assign to me", Exact = true })).ToBeVisibleAsync(); // check""");

            await view.GetByRole(AriaRole.List, new() { Name = "Checks" }).GetByRole(AriaRole.Listitem)
                .Filter(new() { HasText = "dialog closes" }).GetByRole(AriaRole.Button, new() { Name = "Drop this auto check" }).ClickAsync();
            await view.GetByRole(AriaRole.Button).Filter(new() { HasText = "Printer jammed" }).ClickAsync();
            await view.GetByRole(AriaRole.Checkbox, new() { Name = "Unique per run" }).ClickAsync();
            await Expect(code).ToContainTextAsync("FillAsync(Unique(Configuration.Title))");
            // A deletion undone leaves the other edits as they were.
            await view.Keyboard.PressAsync("Delete");
            await Expect(code).Not.ToContainTextAsync("FillAsync");
            await view.Keyboard.PressAsync("Control+z");
            await Expect(code).ToContainTextAsync("FillAsync(Unique(Configuration.Title))");

            await view.GetByRole(AriaRole.Button, new() { Name = "Test the flow" }).ClickAsync();
            await Expect(view.GetByRole(AriaRole.Status, new() { Name = "Test run" })).ToContainTextAsync("Passed", new() { Timeout = 60_000 });

            var report = panel.Tester.Report!;
            var recording = session.Build();
            var again = FlowCodeGenerator.Generate(Recording.FromJson(recording.ToJson()), "NewTicket", "Flows");
            Assert.Multiple(() =>
            {
                Assert.That(report.Outcome, Is.EqualTo(TestOutcome.Passed), report.Failure?.Headline ?? report.Problem);
                Assert.That(report.Code, Does.Not.Contain("ToBeHiddenAsync"), "the auto check dropped");
                Assert.That(site.Tickets, Is.Empty);
                Assert.That(recording.Actions.Where(action => action.Kind != "expect").Select(action => action.Target?.Text),
                    Is.EqualTo(new[] { "New ticket", "Title", "Create ticket" }), "a click in check mode is no action");
                Assert.That(recording.Actions.Where(action => action.Kind == "expect").Select(action => action.Check),
                    Is.EqualTo(new[] { "text", "text" }));
                Assert.That(again.Code, Is.EqualTo(FlowCodeGenerator.Generate(recording, "NewTicket", "Flows").Code),
                    "the edits and checks are kept in the recording's JSON");
            });
            await Expect(app.GetByText("assigned to you")).ToHaveCountAsync(0);
        }
        finally
        {
            await session.StopAsync();
        }
    }

    [Test]
    public async Task Continue_ReplaysInTheRecordedWindow_RecordsNothingMeanwhile_AndGoesOnAfterTheStepsThatPassed()
    {
        using var site = TestSite.Start();
        var actions = 0;
        var first = await StartAsync($"{site.Url}/helpdesk", news => { if (news is CapturedAction) Interlocked.Increment(ref actions); });
        Recording recorded;
        try
        {
            var app = first.Page;
            await app.GetByTestId("new-ticket").ClickAsync();
            await app.Locator("[name='title']").FillAsync("Printer jammed");
            // A button the app shows this once, which the replay does not find.
            await app.EvaluateAsync("() => form.insertAdjacentHTML('beforeend', '<button id=\"later\">Later</button>')");
            await app.Locator("#later").ClickAsync();
            for (var wait = 0; wait < 50 && Volatile.Read(ref actions) < 3; wait++) await Task.Delay(100);
        }
        finally
        {
            recorded = await first.StopAsync();
        }

        var session = await StartAsync(recorded.StartUrl, continues: recorded);
        Recording continued;
        TestReport report;
        IReadOnlyList<string> notReplayed;
        RecordingState whileReplaying;
        int windows;
        try
        {
            await using var tester = new FlowTester(session, new TestOptions { DefaultTimeout = 1000 });
            whileReplaying = session.State;
            report = await tester.ReplayAsync(RecordRequest.Parse(["record", "new-ticket", recorded.StartUrl]), "Flows");
            // As the person keeps recording from where it stopped.
            notReplayed = report.NotReplayed(session.Build());
            session.Log.Remove(notReplayed);
            await session.RequestAsync(new RunEnded(session.Snapshot.Run!.Id, RunOutcome.Failed));
            await session.RequestAsync(new KeepRecording(true));
            await session.Page.GetByRole(AriaRole.Button, new() { Name = "Create ticket" }).ClickAsync();
            for (var wait = 0; wait < 50 && session.Build().Actions.Count < 3; wait++) await Task.Delay(100);
            windows = session.Page.Context.Browser!.Contexts.Sum(context => context.Pages.Count);
        }
        finally
        {
            continued = await session.StopAsync();
        }

        var code = FlowCodeGenerator.Generate(continued, "NewTicket", "Flows").Code;
        Assert.Multiple(() =>
        {
            Assert.That(whileReplaying, Is.EqualTo(RecordingState.Testing), "nothing is recorded while it replays");
            Assert.That(report.Outcome, Is.EqualTo(TestOutcome.Failed), report.Problem);
            Assert.That(report.Failure?.Action, Is.EqualTo(recorded.Actions[2].Id));
            Assert.That(notReplayed, Is.EqualTo(new[] { recorded.Actions[2].Id }));
            Assert.That(windows, Is.EqualTo(1), "it replays in the recorded window");
            Assert.That(continued.Actions.Select(action => action.Target?.Text),
                Is.EqualTo(new[] { "New ticket", "Title", "Create ticket" }), "what was replayed is not recorded again");
            Assert.That(continued.Actions.Take(2).Select(action => action.Id), Is.EqualTo(recorded.Actions.Take(2).Select(action => action.Id)));
            Assert.That(code, Does.Contain("FillAsync(Configuration.Title)").And.Contain("Name = \"Create ticket\""));
        });
    }

    [Test]
    public async Task Rerecord_ReplaysUpToTheStepInTheRecordedWindow_PutsWhatIsDoneNowInItsPlace_AndUndoBringsItBack()
    {
        using var site = TestSite.Start();
        var session = await StartAsync($"{site.Url}/helpdesk");
        try
        {
            await using var panel = await RecorderPanel.OpenAsync(session, RecordRequest.Parse(["record", "new-ticket", session.Options.Url]));
            var app = session.Page;
            var view = app.Context.Browser!.Contexts.Single(context => context != app.Context).Pages.Single();
            var code = view.GetByRole(AriaRole.Region, new() { Name = "Code" });
            var banner = view.GetByRole(AriaRole.Status, new() { Name = "Re-recording" });
            await app.GetByTestId("new-ticket").ClickAsync();
            await Expect(code).ToContainTextAsync("ToBeVisibleAsync");
            await app.Locator("[name='title']").FillAsync("Printer jammed");
            await app.GetByRole(AriaRole.Button, new() { Name = "Create ticket" }).ClickAsync();
            await Expect(code).ToContainTextAsync("ToBeHiddenAsync");

            // The fill again: the flow replays up to it in the recorded window, which opens the dialog again.
            var card = view.Locator("[data-action]").Filter(new() { HasText = "Printer jammed" });
            // Selected, a card shows its buttons, as when pointed at.
            await card.ClickAsync();
            await card.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Re-record") }).ClickAsync();
            await view.GetByRole(AriaRole.Dialog, new() { Name = "Re-record" }).GetByRole(AriaRole.Button, new() { Name = "Re-record", Exact = true }).ClickAsync();
            await Expect(banner).ToContainTextAsync("do it again, then press Done", new() { Timeout = 60_000 });
            await app.Locator("[name='title']").FillAsync("Scanner offline");
            await app.Keyboard.PressAsync("F2");
            await Expect(banner).ToContainTextAsync("Re-recorded");
            await Expect(code).ToContainTextAsync("\"Scanner offline\"");
            var rerecorded = session.Build();
            var state = session.State;
            var pages = app.Context.Pages.Count;

            await view.Keyboard.PressAsync("Control+z");
            await Expect(code).ToContainTextAsync("\"Printer jammed\"");
            // Recorded again once more, then cancelled: the app goes back where it was, and the recording records again.
            if (session.State == RecordingState.Paused) await session.TogglePauseAsync();
            var before = app.Url;
            await card.ClickAsync();
            await card.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Re-record") }).ClickAsync();
            await view.GetByRole(AriaRole.Dialog, new() { Name = "Re-record" }).GetByRole(AriaRole.Button, new() { Name = "Re-record", Exact = true }).ClickAsync();
            await Expect(banner).ToContainTextAsync("do it again, then press Done", new() { Timeout = 60_000 });
            await view.Keyboard.PressAsync("Escape");
            for (var waited = 0; waited < 100 && (session.RerecordingPart is not null || session.State != RecordingState.Recording); waited++)
                await Task.Delay(100);
            var cancelled = (session.State, app.Url);
            Assert.Multiple(() =>
            {
                Assert.That(rerecorded.Actions.Select(action => (action.Target?.Text, action.Value)),
                    Is.EqualTo(new (string?, string?)[] { ("New ticket", null), ("Title", "Scanner offline"), ("Create ticket", null) }),
                    "what was done again is in its place, the step after it stays, and nothing replayed is recorded");
                Assert.That(state, Is.EqualTo(RecordingState.Paused), "the app is in the dialog, not where the flow ends");
                Assert.That(pages, Is.EqualTo(1), "it replays in the recorded window");
                Assert.That(session.Build().Actions.Select(action => action.Value), Has.Member("Printer jammed"));
                Assert.That(cancelled, Is.EqualTo((RecordingState.Recording, before)),
                    "a part cancelled goes back to the page and the state the app was in, never silently paused");
            });
        }
        finally
        {
            await session.StopAsync();
        }
    }

    // Never the person's own login.
    private static Task<RecordingSession> StartAsync(string url, Action<object>? onNews = null, Recording? continues = null) =>
        RecordingSession.StartAsync(new RecordingOptions(url)
        {
            LoadStorageStatePath = null, SaveStorageStatePath = null, BrowserArgs = ["--headless=new"], Continues = continues,
        }, onNews ?? (_ => { }));
}
