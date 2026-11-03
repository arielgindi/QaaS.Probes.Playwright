using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Playwright;
using QaaS.Playwright.Browser;
using QaaS.Playwright.Recorder;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Panel;
using QaaS.Playwright.Tests.EndToEnd;
using static Microsoft.Playwright.Assertions;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>The recorder's panel shows the flow exactly as it will be saved, and takes the person's corrections.</summary>
[TestFixture]
public class RecorderPanelTests
{
    [Test]
    public void Panel_ShowsTheSavedFlow_WithEachActionsLines_AndTheLocatorThePersonChose()
    {
        var recording = Recording.FromJson(File.ReadAllText(Path.Combine(GoldenFolder(), "ScheduleMaintenance.recording.json")));
        var first = recording.Actions[0].Target!;
        first.Chosen = first.Candidates.FindIndex(candidate => candidate.Kind == "row");

        var view = PanelSnapshot.Of(recording, new RecordRequest("ScheduleMaintenance", recording.StartUrl, "Flows"), "Flows",
            status: "recording", error: null).View;

        var saved = FlowCodeGenerator.Generate(recording, "ScheduleMaintenance", "Flows");
        var lines = view.Code.Split('\n');
        var actions = view.Steps.SelectMany(step => step.Actions.Select(action => (Step: step, Action: action))).ToList();
        var clicked = actions[0].Action;
        Assert.Multiple(() =>
        {
            Assert.That(view.Code, Is.EqualTo(saved.Code));
            Assert.That(view.Yaml, Is.EqualTo(saved.Yaml));
            Assert.That(clicked.Picked, Is.EqualTo(first.Chosen), "the person's choice wins");
            Assert.That(clicked.Auto, Is.Not.EqualTo(first.Chosen));
            Assert.That(lines[clicked.Lines!.First - 1],
                Does.Contain("page.GetByRole(AriaRole.Row).Filter(new() { HasText = Configuration.PlateNumber })"));
            // Each action's lines are inside its step's method, after the action before it.
            var previous = 0;
            foreach (var (step, action) in actions)
            {
                Assert.That(action.Lines!.First, Is.GreaterThan(Math.Max(previous, step.Lines!.First)), $"{action.Kind} {action.Element}");
                Assert.That(action.Lines.Last, Is.LessThan(step.Lines.Last), $"{action.Kind} {action.Element}");
                previous = action.Lines.Last;
            }
        });
    }

    [Test]
    public void AStepRecordedAgain_GoesInPlaceOfTheOld_TheStepsAfterItStay_AndUndoBringsTheOldBack()
    {
        var recorded = Recording.FromJson(File.ReadAllText(Path.Combine(GoldenFolder(), "ScheduleMaintenance.recording.json")));
        var request = new RecordRequest("ScheduleMaintenance", recorded.StartUrl, "Flows");
        var log = new CaptureLog(from: recorded);
        var before = FlowCodeGenerator.Generate(log.Build(recorded.StartUrl), request.ClassName, "Flows").Code;
        var shown = PanelSnapshot.Of(log.Build(recorded.StartUrl), request, "Flows", "recording", null);
        var fill = shown.Steps.Single(step => step.Name == "FillScheduleMaintenanceAsync");
        var part = shown.PartOf(fill.Actions[1].Source.Id, wholeStep: true, after: false, fill.Name)!;

        // Done again with only the notes, typed otherwise, as the capture script reports it.
        log.BeginSplice(part.Label, part.Replaced, part.After, part.Before);
        var notes = JsonSerializer.SerializeToNode(recorded.Actions[6], Recording.Json)!.AsObject();
        (notes["type"], notes["id"], notes["t"], notes["value"], notes["raw"]) =
            ("action", "again-1", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "Grinding noise", "Grinding noise");
        log.Add("page", notes.ToJsonString());
        var recording = log.Build(recorded.StartUrl);
        var during = PanelSnapshot.Of(recording, request, "Flows", "recording", null, log.Fresh(recording));
        log.EndSplice(keep: true);
        var again = FlowCodeGenerator.Generate(Recording.FromJson(log.Build(recorded.StartUrl).ToJson()), request.ClassName, "Flows");
        var undo = log.NextUndo(hasSteps: true);
        log.Undo([]);

        Assert.Multiple(() =>
        {
            Assert.That(part.Replaced, Is.EqualTo(recorded.Actions.Skip(2).Take(5).Select(action => action.Id)), "the step, and what led to it");
            Assert.That((part.After, part.Before, part.UpTo), Is.EqualTo((recorded.Actions[1].Id, recorded.Actions[7].Id, recorded.Actions[1].Id)),
                "the replay runs to the step before it");
            Assert.That(recording.Actions.Select(action => action.Id),
                Is.EqualTo(new[] { recorded.Actions[0].Id, recorded.Actions[1].Id, "again-1", recorded.Actions[7].Id }));
            Assert.That(during.Undo, Is.EqualTo(new[] { "again-1" }), "Undo takes back what was recorded in the part first");
            Assert.That(again.Code, Does.Contain("""Notes { get; init; } = "Grinding noise";""").And.Not.Contain("maintenanceTypeId")
                .And.Contain("""Name = "Schedule", Exact = true"""), "kept in the recording's JSON, with the step after it");
            Assert.That(undo, Is.EqualTo("Bring back FillScheduleMaintenanceAsync as it was"));
            Assert.That(FlowCodeGenerator.Generate(log.Build(recorded.StartUrl), request.ClassName, "Flows").Code, Is.EqualTo(before));
        });
    }

    [Test]
    [Category("EndToEnd")]
    public async Task Panel_TakesChoicesUndoAndSave_IsNeverRecorded_AndClosingItLeavesTheRecordingGoing()
    {
        if (ChromeExecutable.Find() is null) Assert.Ignore("Google Chrome is not installed.");
        using var site = TestSite.Start();
        var options = new RecordingOptions($"{site.Url}/helpdesk")
        {
            LoadStorageStatePath = null, SaveStorageStatePath = null, BrowserArgs = ["--headless=new"],
        };
        var session = await RecordingSession.StartAsync(options, _ => { });
        Recording recording;
        try
        {
            await using var panel = await RecorderPanel.OpenAsync(session, RecordRequest.Parse(["record", "new-ticket", options.Url]));
            var app = session.Page;
            var view = session.Page.Context.Browser!.Contexts.Single(context => context != app.Context).Pages.Single();
            var code = view.GetByRole(AriaRole.Region, new() { Name = "Code" });

            await app.GetByRole(AriaRole.Row, new() { Name = "TCK-1042" }).GetByRole(AriaRole.Button).ClickAsync();
            await view.GetByRole(AriaRole.Button).Filter(new() { HasText = "Assign to me" }).ClickAsync();
            await Expect(code).ToContainTextAsync("GetByRole(AriaRole.Row).Filter(new() { HasText = Configuration.Ticket })");

            await view.GetByRole(AriaRole.Radio, new() { Name = "Role and exact name" }).ClickAsync();
            await Expect(code).ToContainTextAsync("await page.GetByRole(AriaRole.Button, new() { Name = \"Assign to me\", Exact = true }).ClickAsync();");
            await Expect(view.GetByText("this locator found 3 elements when recorded").First).ToBeVisibleAsync();

            // Undo takes back the choice first, then the step.
            await view.Keyboard.PressAsync("Control+z");
            await Expect(code).ToContainTextAsync("GetByRole(AriaRole.Row).Filter(new() { HasText = Configuration.Ticket })");
            await view.Keyboard.PressAsync("Control+z");
            await Expect(view.GetByText("Click something in the app to record the first step")).ToBeVisibleAsync();

            await app.GetByTestId("new-ticket").ClickAsync();
            await view.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
            await panel.SaveRequested.WaitAsync(TimeSpan.FromSeconds(10));

            await view.CloseAsync();
            await app.GetByRole(AriaRole.Button, new() { Name = "Create ticket" }).ClickAsync();
            await Task.Delay(500);
            Assert.That(session.Closed.IsCompleted, Is.False, "closing the panel ends nothing");
        }
        finally
        {
            recording = await session.StopAsync();
        }

        Assert.That(recording.Actions.Select(action => action.Target?.Text), Is.EqualTo(new[] { "New ticket", "Create ticket" }),
            "undone actions are left out, and nothing done in the panel is recorded");
    }

    [Test]
    [Category("EndToEnd")]
    public async Task Try_RecordsNothing_ShowsWhatTheRecorderWouldWrite_AndAddsWhatThePersonChoseToTheFlow()
    {
        if (ChromeExecutable.Find() is null) Assert.Ignore("Google Chrome is not installed.");
        using var site = TestSite.Start();
        var options = new RecordingOptions($"{site.Url}/helpdesk")
        {
            LoadStorageStatePath = null, SaveStorageStatePath = null, BrowserArgs = ["--headless=new"],
        };
        var session = await RecordingSession.StartAsync(options, _ => { });
        Recording recording;
        try
        {
            await using var panel = await RecorderPanel.OpenAsync(session, RecordRequest.Parse(["record", "new-ticket", options.Url]));
            var app = session.Page;
            var view = session.Page.Context.Browser!.Contexts.Single(context => context != app.Context).Pages.Single();
            var code = view.GetByRole(AriaRole.Region, new() { Name = "Code" });
            var tried = view.GetByRole(AriaRole.Region, new() { Name = "Tried" });
            await app.GetByTestId("new-ticket").ClickAsync();
            await Expect(code).ToContainTextAsync("ToBeVisibleAsync");

            await app.Keyboard.PressAsync("F7");
            await Expect(tried).ToBeVisibleAsync();
            await app.Locator("[name='title']").FillAsync("Printer jammed");
            await app.GetByRole(AriaRole.Button, new() { Name = "Create ticket" }).ClickAsync();
            var fill = tried.Locator("[data-tried]").Filter(new() { HasText = "Printer jammed" });
            await Expect(fill).ToContainTextAsync("[name='title']");
            await Expect(fill).ToContainTextAsync("Title= 'Printer jammed'");
            await Expect(tried.Locator("[data-tried]").Filter(new() { HasText = "Create ticket" })).ToContainTextAsync("dialog closes");
            await Expect(code).Not.ToContainTextAsync("FillAsync");

            await fill.GetByRole(AriaRole.Button, new() { Name = "Add to flow" }).ClickAsync();
            await Expect(code).ToContainTextAsync("FillAsync(Configuration.Title)");
            await view.Keyboard.PressAsync("F7");
            await Expect(tried).ToHaveCountAsync(0);
        }
        finally
        {
            recording = await session.StopAsync();
        }

        // As after a pause, the flow goes by its address to the page the app was left on.
        Assert.That(recording.Actions.Select(action => (action.Kind, action.Target?.Text, action.Added)),
            Is.EqualTo(new (string, string?, bool)[] { ("click", "New ticket", false), ("fill", "Title", true), ("navigate", null, false) }),
            "only what was added from Try is in the flow");
    }

    [Test]
    [Category("EndToEnd")]
    public async Task ElementsView_PicksAnItemOfAMenuThatClosed_AndAddsItsClick()
    {
        if (ChromeExecutable.Find() is null) Assert.Ignore("Google Chrome is not installed.");
        using var site = TestSite.Start();
        var options = new RecordingOptions($"{site.Url}/orders")
        {
            LoadStorageStatePath = null, SaveStorageStatePath = null, BrowserArgs = ["--headless=new"],
        };
        var session = await RecordingSession.StartAsync(options, _ => { });
        Recording recording;
        try
        {
            await using var panel = await RecorderPanel.OpenAsync(session, RecordRequest.Parse(["record", "archive-order", options.Url]));
            var app = session.Page;
            var view = session.Page.Context.Browser!.Contexts.Single(context => context != app.Context).Pages.Single();

            await app.GetByRole(AriaRole.Button, new() { Name = "Actions" }).ClickAsync();
            await app.Keyboard.PressAsync("F4");
            // The menu closes, as when the focus goes to the panel.
            await view.GetByText("Frozen at").WaitForAsync();
            await app.EvaluateAsync("() => document.activeElement.blur()");
            await Expect(app.GetByRole(AriaRole.Menu)).ToHaveCountAsync(0);

            await view.GetByRole(AriaRole.Treeitem).Filter(new() { HasText = "Archive" }).ClickAsync();
            await Expect(view.GetByText("Not on the page now")).ToBeVisibleAsync();
            await view.GetByRole(AriaRole.Button, new() { Name = "Add click" }).ClickAsync();
            await view.GetByRole(AriaRole.Radio, new() { Name = "Code" }).ClickAsync();
            await Expect(view.GetByRole(AriaRole.Region, new() { Name = "Code" }))
                .ToContainTextAsync("await page.GetByRole(AriaRole.Menuitem, new() { Name = \"Archive\", Exact = true }).ClickAsync();");
        }
        finally
        {
            recording = await session.StopAsync();
        }

        Assert.That(recording.Actions.Select(action => (action.Target?.Text, action.Added)),
            Is.EqualTo(new[] { ("Actions", false), ("Archive", true) }), "F4 and the panel record nothing themselves");
    }

    private static string GoldenFolder([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "Golden");
}
