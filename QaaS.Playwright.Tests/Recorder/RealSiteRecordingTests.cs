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
    public async Task OrangeHrm_RecordedMessily_GivesAFlowATesterKeeps_ThatPassesOnAFreshPage()
    {
        if (ChromeExecutable.Find() is null) Assert.Ignore("Google Chrome is not installed.");
        using var site = TestSite.Start();
        var url = $"{site.Url}/web/index.php/dashboard/index";
        var session = await StartAsync(url);
        try
        {
            var page = session.Page;
            var person = new Person(page);
            ILocator Row(string key) => page.GetByRole(AriaRole.Row).Filter(new() { HasText = key });
            ILocator Group(string label) => page.Locator(".oxd-input-group").Filter(new() { Has = page.GetByText(label, new() { Exact = true }) });

            // Empty space in the side menu, then its link.
            await person.ClickAtAsync(page.Locator(".oxd-sidepanel-body"), fromBottom: 20);
            await person.ClickAsync(page.GetByRole(AriaRole.Link, new() { Name = "Admin" }));
            await page.Locator(".oxd-table-card").First.WaitForAsync();
            await person.ClickAsync(page.GetByRole(AriaRole.Columnheader).Locator(".oxd-checkbox-input"));
            foreach (var user in new[] { "adna.maric.49677", "Dayana.Wisoky", "dtr69884", "manimarann" })
                await person.ClickAsync(Row(user).Locator(".oxd-checkbox-input"));
            await person.ClickAsync(Row("manimarann").Locator("button:has(i.bi-trash)"));
            await person.ClickAsync(page.GetByRole(AriaRole.Button, new() { Name = "Yes, Delete" }));
            foreach (var user in new[] { "Jobinsam@6742", "ksenia.kuz751968", "kmn25326", "nqw35599" })
            {
                await page.Locator(".oxd-overlay:not(.oxd-overlay--hide)").WaitForAsync(new() { State = WaitForSelectorState.Detached });
                await person.ClickAsync(Row(user).Locator(".oxd-checkbox-input"));
                await person.ClickAsync(Row(user).Locator("button:has(i.bi-trash)"));
                await person.ClickAsync(page.GetByRole(AriaRole.Button, new() { Name = "Yes, Delete" }));
            }

            await page.Locator(".oxd-overlay:not(.oxd-overlay--hide)").WaitForAsync(new() { State = WaitForSelectorState.Detached });
            await person.ClickAsync(Group("User Role").Locator(".oxd-select-text-input"));
            await person.ClickAsync(page.GetByRole(AriaRole.Option, new() { Name = "Admin" }));
            await person.ClickAsync(Group("Status").Locator(".oxd-select-text-input"));
            await person.ClickAsync(page.GetByRole(AriaRole.Option, new() { Name = "Disabled" }));
            // The select again, through the value it shows now.
            await person.ClickAsync(page.GetByText("Disabled", new() { Exact = true }));
            await person.ClickAsync(page.GetByRole(AriaRole.Option, new() { Name = "Enabled" }));
            // Its autocomplete: a letter, then a hint.
            await person.TypeAsync(Group("Employee Name").Locator("input"), "a");
            await page.GetByRole(AriaRole.Option, new() { Name = "Joao Santos" }).WaitForAsync();
            await person.ClickAsync(page.GetByRole(AriaRole.Option, new() { Name = "Joao Santos" }));
            await person.ClickAsync(page.GetByRole(AriaRole.Button, new() { Name = "Search" }));
            await person.ClickAsync(page.Locator("button:has(i.bi-caret-down-fill)"));
            await person.ClickAsync(page.Locator("button:has(i.bi-caret-down-fill)"));
            await person.ClickAsync(page.GetByTitle("Help"));
            await person.ClickAsync(page.GetByRole(AriaRole.Link, new() { Name = "My Info" }));
            await page.Locator(".oxd-date-input").First.WaitForAsync();

            await person.ClickAsync(page.GetByText("Single", new() { Exact = true }));
            await person.ClickAsync(page.GetByRole(AriaRole.Option, new() { Name = "Other" }));
            ILocator Section(string heading) => page.Locator(".orangehrm-horizontal-padding, .orangehrm-card-container")
                .Filter(new() { Has = page.GetByText(heading, new() { Exact = true }) });
            await person.ClickAsync(Section("Personal Details").GetByRole(AriaRole.Button, new() { Name = "Save" }));
            var expiry = Group("License Expiry Date");
            await person.ClickAsync(expiry.Locator("i.oxd-date-input-icon"));
            await person.ClickAsync(expiry.Locator(".oxd-calendar-date").GetByText("13", new() { Exact = true }));
            await person.ClickAsync(expiry.Locator("i.oxd-date-input-icon"));
            await person.ClickAsync(expiry.Locator(".oxd-calendar-selector-month-selected"));
            await person.ClickAsync(expiry.Locator(".oxd-calendar-dropdown--option").GetByText("March", new() { Exact = true }));
            await person.ClickAsync(expiry.Locator(".oxd-calendar-selector-year-selected"));
            await person.ClickAsync(expiry.Locator(".oxd-calendar-dropdown--option").GetByText("2027", new() { Exact = true }));
            await person.ClickAsync(expiry.Locator(".oxd-calendar-date").GetByText("22", new() { Exact = true }));
            await person.ClickAsync(Section("Personal Details").GetByRole(AriaRole.Button, new() { Name = "Save" }));
            await person.ClickAsync(page.GetByText("A+", new() { Exact = true }));
            await person.ClickAsync(page.GetByRole(AriaRole.Option, new() { Name = "B-" }));
            await person.TypeAsync(Group("Test_Field").Locator("input"), "124445");
            await page.Keyboard.PressAsync("Enter");
            await person.ClickAsync(page.Locator(".orangehrm-action-header").Filter(new() { HasText = "Attachments" }).GetByRole(AriaRole.Button));
            await person.ClickAsync(page.GetByText("Browse", new() { Exact = true }));
            await person.TypeAsync(page.GetByPlaceholder("Type comment here"), "klasmvlksadmvklasdmv");
            await person.ClickAsync(Section("Add Attachment").GetByRole(AriaRole.Button, new() { Name = "Save" }));
            await person.ClickAsync(page.Locator(".oxd-switch-input"));
            for (var times = 0; times < 2; times++) await person.ClickAsync(page.GetByText("Contract Start Date", new() { Exact = true }));
            // The tab's link, dragged a little as it was clicked, then clicked.
            await person.DragAsync(page.GetByRole(AriaRole.Link, new() { Name = "Personal Details" }), 14);
            await person.ClickAsync(page.GetByRole(AriaRole.Link, new() { Name = "Buzz" }));
            await page.Locator("#heart-svg").First.WaitForAsync();
            await person.ClickAsync(page.Locator(".orangehrm-buzz").Filter(new() { HasText = "Joao Santos" }).Locator("#heart-svg"));
            await Task.Delay(500);

            var plan = FlowPlanner.Plan(session.Build(), "UsersAndMyInfo");
            var flow = FlowWriter.Write(plan, "Flows");
            TestContext.Out.WriteLine(flow.Code);
            TestContext.Out.WriteLine(flow.Yaml);
            foreach (var suggestion in plan.Suggestions) TestContext.Out.WriteLine($"suggests: {suggestion.Text}");
            Apply(session, plan);
            var applied = FlowCodeGenerator.Generate(session.Build(), "UsersAndMyInfo", "Flows");
            TestContext.Out.WriteLine(applied.Code);
            var report = await TestAsync(session, url);

            Assert.Multiple(() =>
            {
                Assert.That(flow.Code, Does.Not.Contain("Name = \"\"").And.Not.Contain("GetByLabel(\"\"").And.Not.Contain(".Nth(")
                    .And.Not.Match(@"\.(First|Last)\b(?!Name)"), "no empty name, no position");
                Assert.That(flow.Code, Does.Not.Contain("AdminPIM").And.Not.Contain("NameEmployee").And.Not.Contain("Required Cancel")
                    .And.Not.Contain("AscendingDescending").And.Not.Contain("GetByText(\"13\")").And.Not.Contain("GetByText(\"22\")")
                    .And.Not.Contain("\"Browse\"").And.Not.Contain("DragToAsync").And.Not.Contain("HoverAsync"),
                    "no glued text, no calendar's day, no file chooser's click, no drag that dropped nowhere");
                Assert.That(flow.Code, Does.Contain("foreach (var username in Configuration.UsernamesToDelete)")
                    .And.Contain("Locator(\"button:has(i.bi-trash)\")").And.Contain("GetByRole(AriaRole.Checkbox).CheckAsync(new() { Force = true })"),
                    "the delete rounds as a loop over the rows' keys, the trash by its icon, the checkbox through the box over it");
                Assert.That(flow.Code, Does.Contain("Configuration.UsernamesToUncheck"), "the unticks as a loop");
                Assert.That(flow.Code, Does.Contain("GetByRole(AriaRole.Columnheader).GetByRole(AriaRole.Checkbox)"), "select all by its place");
                Assert.That(flow.Code, Does.Contain("Filter(new() { Has = page.GetByText(\"User Role\", new() { Exact = true }) })")
                    .And.Contain("Filter(new() { Has = page.GetByText(\"Status\", new() { Exact = true }) })"), "each select by its group's label");
                Assert.That(flow.Code, Does.Contain("GetByText(\"Personal Details\", new() { Exact = true })")
                    .And.Contain("GetByText(\"Attachments\", new() { Exact = true })"), "a Save and an Add of several by their section");
                Assert.That(flow.Code, Does.Contain("GetByTitle(\"Help\", new() { Exact = true })")
                    .And.Contain("Locator(\"button:has(i.bi-caret-down-fill)\")"), "icon buttons by their title and icon");
                Assert.That(flow.Code, Does.Contain("FillAsync(Configuration.LicenseExpiryDate)"), "the date as typed");
                Assert.That(flow.Code, Does.Contain("Filter(new() { HasText = \"Joao Santos\" }).Locator(\"[id='heart-svg']\")"), "the post's like by its words");
                Assert.That(flow.Code, Does.Not.Match(@"[a-z]\d+Async\("), "no numbered step");
                Assert.That(flow.Code, Does.Contain("FillAsync(Configuration.EmployeeName)"), "the autocomplete typed its hint's own text");
                Assert.That(flow.Yaml, Does.Contain("UserRole: 'Admin'").And.Contain("EmployeeName: 'Joao Santos'").And.Contain("MaritalStatus: 'Other'").And.Contain("BloodType: 'B-'")
                    .And.Contain("TestField: '124445'").And.Contain("IncludeEmploymentContractDetails: true").And.Contain("UsernamesToDelete:")
                    .And.Not.Contain("Admin: 'Admin'").And.Not.Contain("Value").And.Not.Contain("B:"), "settings by the groups' labels");
                Assert.That(plan.Suggestions.Select(suggestion => suggestion.Key.Split(':')[0]), Does.Contain("replaced"),
                    "Status and the date chosen again are suggested");
                Assert.That(report.Outcome, Is.EqualTo(TestOutcome.Passed), $"{report.Failure?.Headline ?? report.Problem}\n{applied}");
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
        // OrangeHRM's attachment, had a file been chosen: its group's label and section name it.
        var file = Captured.Element("button", "", Captured.Css("generator", "input.oxd-file-input"));
        (file.Tag, file.Type, file.GroupLabel, file.Section) = ("input", "file", "Select File", "Attachments");
        var upload = Captured.Action("upload", file);
        upload.Files = ["report.pdf"];
        var uploaded = FlowCodeGenerator.Generate(Captured.Recording(upload), "Attach", "Flows");

        Assert.Multiple(() =>
        {
            Assert.That(demoqaFlow.Code, Does.Not.Contain("css-").And.Not.Contain("CloseCount"), "no hashed class, no loop of idle clicks");
            Assert.That(demoqaFlow.Code, Does.Contain("foreach (var hobby in Configuration.Hobbies)"), "the hobbies by their labels");
            Assert.That(demoqaFlow.Code, Does.Not.Contain("SubjectsInputSearch").And.Not.Contain("[id='subjectsInput']\").ClickAsync()"),
                "neither filter letters nor focus clicks");
            Assert.That(appliedFlow.Code, Does.Contain("foreach (var subject in Configuration.Subjects)"), "the subjects left a list");
            Assert.That(hrmFlow.Code + hrmFlow.Yaml, Does.Not.Contain("typed-secret-1"), "no password in the flow or its YAML");
            Assert.That(Autosave.WithoutSecrets(hrm).ToJson(), Does.Not.Contain("typed-secret-1"),
                "nor in the raw recording --keep-recording keeps");
            Assert.That(hrmFlow.Yaml, Does.Contain("Password: '${PASSWORD}'"), "the YAML reads it from the environment");
            Assert.That(hrmFlow.Code, Does.Not.Contain("FillEmpNumber").And.Not.Contain("drag").And.Not.Contain("HoverAsync"),
                "no route key as a name, no drag that dropped nowhere");
            Assert.That(hrmFlow.Code, Does.Contain("GetByRole(AriaRole.Link, new() { Name = \"Admin\", Exact = true })"),
                "a menu's link is no user name typed to log in");
            Assert.That(hrmFlow.Code, Does.Contain("CloseSharePhotosAsync").And.Contain("Keyboard.PressAsync(\"Escape\")"),
                "a dialog that closed with nothing seen is closed by Escape");
            Assert.That(uploaded.Code + uploaded.Yaml, Does.Contain("SetInputFilesAsync(Configuration.AttachmentFile)")
                .And.Contain("AttachmentFile: 'report.pdf'").And.Not.Contain("FileToUpload("), "a file uploaded is a setting of its path");
        });
    }

    [Test]
    public void RawRecordings_OfReactAdminAntDesignProAndKendo_GiveFlowsThatReplay()
    {
        // react-admin's demo logs in as demo, with the password demo: typed live, though no file of the recorder keeps it.
        var customers = Read("ReactAdminCustomers");
        foreach (var typed in customers.Actions.Where(action => action.Target?.Type == "password")) (typed.Value, typed.Raw) = ("demo", "demo");
        var customersFlow = FlowCodeGenerator.Generate(customers, "CustomersFilterEdit", "Flows");

        Assert.Multiple(() =>
        {
            Assert.That(customersFlow.Code, Does.Contain("Locator(\"[name='password']\").FillAsync(Configuration.Password)")
                .And.Not.Contain("[name='password']\").FillAsync(Configuration.Username)"),
                "a password the user name equals is the password's setting");
            Assert.That(customersFlow.Yaml, Does.Contain("Username: 'demo'").And.Contain("Password: '${PASSWORD}'").And.Not.Contain("Password: 'demo'"),
                "the YAML reads it from the environment");
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
        return await tester.RunAsync(RecordRequest.Parse(["record", "real-site", url]), "Flows");
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

        public async Task ClickAtAsync(ILocator element, float fromBottom)
        {
            var box = await element.BoundingBoxAsync() ?? throw new InvalidOperationException("The element does not show.");
            await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height - fromBottom, new() { Steps = 3 });
            await page.Mouse.DownAsync();
            await page.Mouse.UpAsync();
            await Task.Delay(300);
        }

        // Presses, moves a little and lets go, as a click does that slipped.
        public async Task DragAsync(ILocator element, float by)
        {
            await MoveAsync(element);
            var box = (await element.BoundingBoxAsync())!;
            await page.Mouse.DownAsync();
            await page.Mouse.MoveAsync(box.X + box.Width / 2 + by, box.Y + box.Height / 2 + 2, new() { Steps = 4 });
            await page.Mouse.UpAsync();
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
