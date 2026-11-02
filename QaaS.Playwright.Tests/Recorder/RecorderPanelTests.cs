using System.Runtime.CompilerServices;
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
            saving: false, error: null).View;

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

    private static string GoldenFolder([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "Golden");
}
