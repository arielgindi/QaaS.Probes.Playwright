using System.Runtime.CompilerServices;
using Microsoft.Playwright;
using QaaS.Playwright.Browser;
using QaaS.Playwright.Recorder;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Generation;
using QaaS.Playwright.Recorder.TestRun;
using QaaS.Playwright.Tests.EndToEnd;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>
/// What people recorded on two public practice sites, clicking around as people do: demoqa's practice form (react-select,
/// a multi-select of subjects, checkboxes, a fading Bootstrap modal) and an OrangeHRM session (custom selects in form
/// groups with labels tied to nothing, icon buttons, a table whose rows are deleted through a dialog, several forms with a
/// Save each, a date picker). Each is replayed on the test site's imitation of the site, with the mouse and the keyboard as
/// the person did, in the same messy order; the flow must be one a tester keeps, pass the panel's Test on a fresh load,
/// and pass again with the recorder's suggestions applied, on a page whose hashed classes and numbered ids changed. The
/// raw recordings the recorder of 2.0.1 made on the real sites are read too, for what they can tell.
/// </summary>
[TestFixture]
[Category("EndToEnd")]
public class RealSiteRecordingTests
{
    [Test]
    public async Task PracticeForm_RecordedMessily_GivesAFlowATesterKeeps_ThatPassesOnFreshPages()
    {
        if (ChromeExecutable.Find() is null) Assert.Ignore("Google Chrome is not installed.");
        using var site = TestSite.Start();
        var url = $"{site.Url}/automation-practice-form";
        var session = await StartAsync(url);
        try
        {
            var page = session.Page;
            var person = new Person(page);
            await person.TypeAsync(page.Locator("#userNumber"), "1234");
            await page.Keyboard.PressAsync("Enter");
            await person.ClickAsync(page.Locator("#state div[aria-hidden='true']"));
            await person.ClickAsync(page.GetByRole(AriaRole.Option, new() { Name = "Haryana" }));
            await person.ClickAsync(page.Locator("#city div[aria-hidden='true']"));
            await person.ClickAsync(page.GetByRole(AriaRole.Option, new() { Name = "Karnal" }));
            foreach (var hobby in new[] { "Music", "Reading", "Sports" })
                await person.ClickAsync(page.Locator("label").Filter(new() { HasText = hobby }));
            // A stray click into Subjects, then on to the gender.
            await person.ClickAsync(page.Locator("#subjectsContainer"));
            await person.ClickAsync(page.Locator("label[for='gender-radio-1']"));
            await person.TypeAsync(page.Locator("#userEmail"), "blabla@gmail.com");
            await page.Keyboard.PressAsync("Enter");
            await person.TypeAsync(page.Locator("#firstName"), "hello");
            await person.TypeAsync(page.Locator("#lastName"), "helkasmlck");
            await person.TypeAsync(page.Locator("#userNumber"), "1234543523", replace: true);
            await person.TypeAsync(page.Locator("#subjectsContainer"), "1231 ");
            await person.ClickAsync(page.Locator("h5"));
            await person.TypeAsync(page.Locator("#subjectsContainer"), "s");
            await person.ClickAsync(page.GetByRole(AriaRole.Option, new() { Name = "Maths" }));
            // On the chip just chosen, then the next subject's letters.
            await person.ClickAsync(page.Locator(".subjects-auto-complete__multi-value__label").Filter(new() { HasText = "Maths" }));
            await page.Keyboard.TypeAsync("p", new() { Delay = 40 });
            await person.ClickAsync(page.GetByRole(AriaRole.Option, new() { Name = "Physics" }));
            await page.Keyboard.TypeAsync("a", new() { Delay = 40 });
            await person.ClickAsync(page.GetByRole(AriaRole.Option, new() { Name = "Arts", Exact = true }));
            await person.ClickAsync(page.GetByRole(AriaRole.Button, new() { Name = "Remove Physics" }));
            await person.ClickAsync(page.Locator("#city div[aria-hidden='true']"));
            await person.ClickAsync(page.GetByRole(AriaRole.Option, new() { Name = "Panipat" }));
            await person.ClickAsync(page.Locator("#submit"));
            for (var times = 0; times < 4; times++) await person.ClickAsync(page.Locator("#closeLargeModal"));
            await page.Keyboard.PressAsync("Escape");
            await page.GetByRole(AriaRole.Dialog).WaitForAsync(new() { State = WaitForSelectorState.Detached });
            await Task.Delay(500);

            var plan = FlowPlanner.Plan(session.Build(), "PracticeForm");
            var flow = FlowWriter.Write(plan, "Flows");
            TestContext.Out.WriteLine(flow.Code);
            TestContext.Out.WriteLine(flow.Yaml);
            foreach (var suggestion in plan.Suggestions) TestContext.Out.WriteLine($"suggests: {suggestion.Text}");
            var report = await TestAsync(session, url);

            // What the person would apply, one click each in the panel.
            Apply(session, plan);
            var applied = FlowCodeGenerator.Generate(session.Build(), "PracticeForm", "Flows");
            TestContext.Out.WriteLine(applied.Code);
            TestContext.Out.WriteLine(applied.Yaml);
            var again = await TestAsync(session, url);

            Assert.Multiple(() =>
            {
                Assert.That(flow.Code, Does.Not.Contain("css-").And.Not.Contain("react-select-").And.Not.Contain(".Nth(")
                    .And.Not.Match(@"\.(First|Last)\b(?!Name)").And.Not.Contain("Name = \"\"").And.Not.Contain("name@example.com"),
                    "no hashed class, instance id, position or example");
                Assert.That(flow.Code, Does.Contain("Locator(\"[id='state']\").GetByRole(AriaRole.Combobox)")
                    .And.Contain("Locator(\"[id='city']\").GetByRole(AriaRole.Combobox)"), "State and City by their wrappers' ids");
                Assert.That(flow.Code, Does.Contain("foreach (var hobby in Configuration.Hobbies)")
                    .And.Contain("GetByRole(AriaRole.Checkbox, new() { Name = hobby, Exact = true }).CheckAsync()"), "the hobbies by their labels");
                Assert.That(flow.Code, Does.Contain("foreach (var subject in Configuration.Subjects)")
                    .And.Contain("FillAsync(subject)"), "the subjects as a list, typed to filter as part of each choice");
                Assert.That(flow.Code, Does.Not.Contain("CloseCount").And.Not.Contain("for (var i"), "no loop of clicks that did nothing");
                Assert.That(flow.Code, Does.Contain("PressAsync(\"Escape\")").And.Contain("ToBeHiddenAsync"), "the modal closed by Escape, as it faded");
                Assert.That(flow.Yaml, Does.Contain("State: 'Haryana'").And.Contain("City").And.Contain("Email: 'blabla@gmail.com'").And.Contain("Subjects:")
                    .And.Not.Contain("Search").And.Not.Contain("Value:").And.Not.Contain("Value2"), "settings by labels and ids");
                Assert.That(plan.Suggestions.Select(suggestion => suggestion.Key.Split(':')[0]),
                    Is.SupersetOf(new[] { "replaced", "removed", "repeated" }), "the noise is suggested, never dropped silently");
                Assert.That(report.Outcome, Is.EqualTo(TestOutcome.Passed), $"{report.Failure?.Headline ?? report.Problem}\n{flow.Code}");

                Assert.That(applied.Code, Does.Not.Contain("Subjects2"), "suggestions applied");
                Assert.That(applied.Yaml, Does.Contain("- 'Maths'\n      - 'Arts'").And.Not.Contain("Physics").And.Not.Contain("Karnal")
                    .And.Not.Contain("'1234'").And.Not.Contain("1231"), "the net choices, the last values");
                Assert.That(again.Outcome, Is.EqualTo(TestOutcome.Passed), $"{again.Failure?.Headline ?? again.Problem}\n{applied.Code}");
            });
        }
        finally
        {
            await session.StopAsync();
        }
    }

    [Test]
    public void RawRecordings_OfTheRealSites_GiveWhatTheyCanTell()
    {
        var demoqa = Read("PracticeForm");
        var hrm = Read("AddUser");
        var demoqaFlow = FlowCodeGenerator.Generate(demoqa, "PracticeForm", "Flows");
        var hrmFlow = FlowCodeGenerator.Generate(hrm, "AddUser", "Flows");
        var demoqaPlan = FlowPlanner.Plan(demoqa, "PracticeForm");
        var appliedFlow = FlowCodeGenerator.Generate(Applied(demoqa, demoqaPlan), "PracticeForm", "Flows");

        Assert.Multiple(() =>
        {
            Assert.That(demoqaFlow.Code, Does.Not.Contain("css-").And.Not.Contain("CloseCount"), "no hashed class, no loop of idle clicks");
            Assert.That(demoqaFlow.Code, Does.Contain("foreach (var hobby in Configuration.Hobbies)"), "the hobbies by their labels");
            Assert.That(demoqaFlow.Code, Does.Not.Contain("SubjectsInputSearch").And.Not.Contain("[id='subjectsInput']\").ClickAsync()"),
                "neither filter letters nor focus clicks");
            Assert.That(appliedFlow.Code, Does.Contain("foreach (var subject in Configuration.Subjects)"), "the subjects left a list");
            Assert.That(hrmFlow.Code + hrmFlow.Yaml, Does.Not.Contain("admin123"), "no password in the flow or its YAML");
            Assert.That(hrmFlow.Yaml, Does.Contain("Password: '${PASSWORD}'"), "the YAML reads it from the environment");
            Assert.That(hrmFlow.Code, Does.Not.Contain("FillEmpNumber").And.Not.Contain("drag").And.Not.Contain("HoverAsync"),
                "no route key as a name, no drag that dropped nowhere");
        });
    }

    // ── Doing as the person did ─────────────────────────────────────────────────────────────────────────────────

    private static Task<RecordingSession> StartAsync(string url) => RecordingSession.StartAsync(new RecordingOptions(url)
    {
        LoadStorageStatePath = null, SaveStorageStatePath = null, BrowserArgs = ["--headless=new"],
    }, _ => { });

    // Tests the flow recorded so far as the panel does: built as saved, in a window of its own, on a fresh page.
    private static async Task<TestReport> TestAsync(RecordingSession session, string url)
    {
        await using var tester = new FlowTester(session, new TestOptions { DefaultTimeout = 5000 });
        return await tester.RunAsync(RecordRequest.Parse(["record", "practice", url]), "Flows");
    }

    // Each suggestion applied, as its one click in the panel does: its actions and what only led to them left out.
    private static void Apply(RecordingSession session, FlowPlan plan) =>
        session.Log.Remove([.. Dropped(session.Build(), plan)]);

    private static Recording Applied(Recording recording, FlowPlan plan)
    {
        var dropped = Dropped(recording, plan);
        return recording with { Actions = [.. recording.Actions.Where(action => !dropped.Contains(action.Id))] };
    }

    private static HashSet<string> Dropped(Recording recording, FlowPlan plan)
    {
        var sources = FlowPlanner.Sources(recording, [.. plan.Steps.SelectMany(step => step.Actions)]);
        return [.. plan.Suggestions.SelectMany(suggestion => suggestion.Drop).SelectMany(id => sources.GetValueOrDefault(id) ?? [id])];
    }

    private static Recording Read(string name) =>
        Recording.FromJson(File.ReadAllText(Path.Combine(Folder(), $"{name}.recording.json")));

    private static string Folder([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "RealSites");

    /// <summary>A person: moves the pointer onto the middle of an element, rests, presses and lets go; types with pauses.</summary>
    private sealed class Person(IPage page)
    {
        public async Task MoveAsync(ILocator element)
        {
            await element.ScrollIntoViewIfNeededAsync();
            var box = await element.BoundingBoxAsync() ?? throw new InvalidOperationException("The element does not show.");
            await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2, new() { Steps = 3 });
            await Task.Delay(80);
        }

        public async Task ClickAsync(ILocator element)
        {
            await MoveAsync(element);
            await page.Mouse.DownAsync();
            await Task.Delay(40);
            await page.Mouse.UpAsync();
            // What the page does is told with the click before the next one begins.
            await Task.Delay(300);
        }

        public async Task TypeAsync(ILocator element, string text, bool replace = false)
        {
            await ClickAsync(element);
            if (replace) await page.Keyboard.PressAsync("Control+A");
            await page.Keyboard.TypeAsync(text, new() { Delay = 30 });
            await Task.Delay(100);
        }
    }
}
