using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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
                    .And.Contain("FillAsync(subject.Search)").And.Contain("Name = subject.Name"),
                    "the subjects as a list, each what was typed to find it and the option taken");
                Assert.That(flow.Code, Does.Not.Contain("CloseCount").And.Not.Contain("for (var i"), "no loop of clicks that did nothing");
                Assert.That(flow.Code, Does.Contain("PressAsync(\"Escape\")").And.Contain("ToBeHiddenAsync"), "the modal closed by Escape, as it faded");
                Assert.That(flow.Yaml, Does.Contain("State: 'Haryana'").And.Contain("City").And.Contain("Email: 'blabla@gmail.com'").And.Contain("Subjects:")
                    .And.Not.Contain("InputSearch").And.Not.Contain("Value:").And.Not.Contain("Value2"), "settings by labels and ids");
                Assert.That(plan.Suggestions.Select(suggestion => suggestion.Key.Split(':')[0]),
                    Is.SupersetOf(new[] { "replaced", "removed", "repeated" }), "the noise is suggested, never dropped silently");
                Assert.That(report.Outcome, Is.EqualTo(TestOutcome.Passed), $"{report.Failure?.Headline ?? report.Problem}\n{flow.Code}");

                Assert.That(applied.Code, Does.Not.Contain("Subjects2"), "suggestions applied");
                Assert.That(applied.Yaml, Does.Contain("- Search: 's'\n        Name: 'Maths'\n      - Search: 'a'\n        Name: 'Arts'")
                    .And.Not.Contain("Physics").And.Not.Contain("Karnal")
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
                Assert.That(flow.Code, Does.Contain("FillAsync(Configuration.EmployeeNameSearch)")
                    .And.Contain("Name = Configuration.EmployeeName, Exact = true"), "the autocomplete typed as the person did, then its hint taken");
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
    public async Task ReactAdminAntDesignProAndKendo_RecordedMessily_GiveFlowsThatReplayOnFreshPages()
    {
        if (ChromeExecutable.Find() is null) Assert.Ignore("Google Chrome is not installed.");
        using var site = TestSite.Start();
        // Each site in a Chrome of its own, at the same time, as each takes its time to be clicked through.
        var reactAdmin = RecordAsync($"{site.Url}/react-admin-demo/", "PostersGalore", ReactAdminAsync);
        var antd = RecordAsync($"{site.Url}/antd-pro/list", "SearchTable", AntDesignProAsync);
        var kendo = RecordAsync($"{site.Url}/kendo-ui/grid", "KendoDemos", KendoAsync);
        await Task.WhenAll(reactAdmin, antd, kendo);
        var (posters, antdRun, kendoRun) = (await reactAdmin, await antd, await kendo);

        Assert.Multiple(() =>
        {
            Assert.That(antdRun.Actions.Where(action => action.Widget == "switch").Select(action => action.Kind),
                Is.EqualTo(new[] { "check", "uncheck", "check" }), "a switch React renders after the click is told as it then shows");
            Assert.That(kendoRun.Actions.Where(action => action.Widget == "switch").Select(action => action.Kind),
                Is.EqualTo(new[] { "check" }), "and one its own handler turns");
            Assert.That(posters.Flow.Code, Does.Contain("FillAsync(Configuration.CustomerSearch)")
                .And.Contain("GetByRole(AriaRole.Option, new() { Name = Configuration.Customer, Exact = true })"),
                "an autocomplete types what the person typed, then takes the option");
            Assert.That(posters.Flow.Yaml + kendoRun.Flow.Yaml, Does.Contain("CustomerSearch: 'Hipol'").And.Contain("Customer: 'Hipolito Bergnaum'")
                .And.Contain("SportSearch: 'Bask'").And.Contain("Sport: 'Basketball'"), "the letters typed, never the suggestion completed in place");
            Assert.That(posters.Flow.Code, Does.Contain("GetByRole(AriaRole.Link, new() { NameRegex = new Regex(\"^Black & White 14\\\\.4x17\\\\.88\") })")
                .And.Not.Contain("US$"), "a poster's link by its title and size, never by the price the demo makes anew on each load");
            Assert.That(antdRun.Flow.Code, Does.Contain(".GetByText(Configuration.Status, new() { Exact = true })").And.Not.Contain("[object Object]"),
                "an option of no role is a choice of its select, found by its text among the options that show");
            Assert.That(antdRun.Flow.Yaml, Does.Contain("Status: 'running'").And.Contain("RuleName: 'Nightly sync'"), "the choice a setting");
            Assert.That(antdRun.Flow.Code, Does.Match(@"\.HoverAsync\(\);\s+await page\.GetByRole\(AriaRole\.Menuitem, new\(\) \{ Name = ""Account Settings"""),
                "a menu that opened on hover is hovered open");
            Assert.That(antdRun.Flow.Code, Does.Match(@"time\.FillAsync\(Configuration\.Time\);\s+await time\.PressAsync\(""Enter""\)"),
                "a date its picker keeps once OK commits it is committed by Enter");
            Assert.That(kendoRun.Flow.Code, Does.Not.Match(@"id=\\?[""']_?[0-9a-f]{8}-").And.Contain("DragToAsync(").And.Not.Contain("Home Ec on Tuesday"),
                "no GUID; the event dropped on the slot it went to, as it was before the drop");
            foreach (var run in new[] { posters, antdRun, kendoRun })
                Assert.That(run.Report.Outcome, Is.EqualTo(TestOutcome.Passed), $"{run.Report.Failure?.Headline ?? run.Report.Problem}\n{run.Flow.Code}");
        });
    }

    private sealed record SiteRun(IReadOnlyList<CapturedAction> Actions, FlowPlan Plan, GeneratedFlow Flow, TestReport Report);

    // Records on the page as the person does, then plans, writes and tests the flow on a fresh page, as the panel does.
    private static async Task<SiteRun> RecordAsync(string url, string name, Func<IPage, Person, Task> person)
    {
        var session = await StartAsync(url);
        try
        {
            await person(session.Page, new Person(session.Page));
            await Task.Delay(500);
            var recording = session.Build();
            var plan = FlowPlanner.Plan(recording, name);
            var flow = FlowWriter.Write(plan, "Flows");
            TestContext.Out.WriteLine(flow.Code);
            TestContext.Out.WriteLine(flow.Yaml);
            foreach (var suggestion in plan.Suggestions) TestContext.Out.WriteLine($"suggests: {suggestion.Text}");
            return new SiteRun(recording.Actions, plan, flow, await TestAsync(session, url));
        }
        finally
        {
            await session.StopAsync();
        }
    }

    // react-admin: logs in as demo / demo, finds a customer's order by the first letters of the name, deletes it, then
    // changes a poster's price.
    private static async Task ReactAdminAsync(IPage page, Person person)
    {
        await person.TypeAsync(page.Locator("#username"), "demo");
        await person.TypeAsync(page.Locator("#password"), "demo");
        await person.ClickAsync(page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }));
        await person.ClickAsync(page.GetByRole(AriaRole.Menuitem, new() { Name = "Orders" }));
        await person.TypeAsync(page.Locator("#customer"), "Hipol");
        await person.ClickAsync(page.GetByRole(AriaRole.Option, new() { Name = "Hipolito Bergnaum" }));
        await person.ClickAsync(page.GetByRole(AriaRole.Row).Filter(new() { HasText = "K7ZM4A" }).GetByRole(AriaRole.Checkbox));
        await person.ClickAsync(page.GetByRole(AriaRole.Button, new() { Name = "Delete" }));
        await person.ClickAsync(page.GetByRole(AriaRole.Menuitem, new() { Name = "Posters" }));
        await person.ClickAsync(page.GetByRole(AriaRole.Link).Filter(new() { HasText = "Black & White" }));
        await person.TypeAsync(page.Locator("#price"), "16.90", replace: true);
        await person.ClickAsync(page.GetByRole(AriaRole.Button, new() { Name = "Save" }));
        await page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "Poster updated" }).WaitForAsync();
    }

    // Ant Design Pro: the settings' switches, turned on, off again, and another on.
    private static async Task AntDesignProAsync(IPage page, Person person)
    {
        ILocator Switch(string label) => page.Locator("li.ant-list-item").Filter(new() { HasText = label }).GetByRole(AriaRole.Switch);
        await person.ClickAsync(Switch("Fixed Header"));
        await person.ClickAsync(Switch("Fixed Header"));
        await person.ClickAsync(Switch("Split Menus"));
        // The Status select: its list opens in a portal, its options of no role, each titled "[object Object]".
        await person.ClickAsync(page.Locator("#status").Locator(".."));
        await person.ClickAsync(page.Locator(".ant-select-item-option:visible").Filter(new() { HasText = "running" }));
        await person.ClickAsync(page.GetByRole(AriaRole.Button, new() { Name = "Query" }));
        // The avatar's menu opens as the pointer rests on it.
        await Task.Delay(1100);
        await person.MoveAsync(page.Locator("#avatar"));
        await Task.Delay(300);
        await person.ClickAsync(page.GetByRole(AriaRole.Menuitem, new() { Name = "Account Settings" }));
        // A new rule, its starting time picked in the calendar and kept by OK.
        await person.ClickAsync(page.GetByRole(AriaRole.Button, new() { Name = "plus New" }));
        await person.TypeAsync(page.Locator("#rule-name"), "Nightly sync");
        await person.ClickAsync(page.Locator("#time"));
        await person.ClickAsync(page.Locator("td[title='2026-11-17']"));
        await person.ClickAsync(page.Locator(".ant-picker-footer button"));
        await person.ClickAsync(page.GetByRole(AriaRole.Button, new() { Name = "Finish" }));
        await page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "Added" }).WaitForAsync();
    }

    // Kendo UI: a product's price changed in its popup, two deleted through the browser's confirm, the switch turned on.
    private static async Task KendoAsync(IPage page, Person person)
    {
        ILocator Row(string product) => page.GetByRole(AriaRole.Row).Filter(new() { HasText = product });
        page.Dialog += (_, dialog) => dialog.AcceptAsync();
        await person.ClickAsync(Row("Chang").GetByRole(AriaRole.Button, new() { Name = "Edit" }));
        await person.TypeAsync(page.Locator("#UnitPrice"), "12.5", replace: true);
        await person.ClickAsync(page.GetByRole(AriaRole.Button, new() { Name = "Update" }));
        await person.ClickAsync(Row("Tofu").GetByRole(AriaRole.Button, new() { Name = "Delete" }));
        await person.ClickAsync(Row("Konbu").GetByRole(AriaRole.Button, new() { Name = "Delete" }));
        await person.ClickAsync(page.GetByRole(AriaRole.Switch, new() { Name = "All day" }));
        // Two files of the tree ticked, by checkboxes whose ids are new GUIDs on each load.
        foreach (var file in new[] { "February.pdf", "March.pdf" })
            await person.ClickAsync(page.GetByRole(AriaRole.Treeitem, new() { Name = file }).GetByRole(AriaRole.Checkbox));
        // The event dragged from Monday 10:00 to Tuesday 9:00.
        await person.DragToAsync(page.Locator(".k-event"), page.Locator(".k-slot[data-day^=Tuesday][data-hour='9:00 AM']"));
        // The combobox completes "Bask" in place; the person takes the option.
        await person.TypeAsync(page.Locator("#sport"), "Bask");
        await person.ClickAsync(page.GetByRole(AriaRole.Option, new() { Name = "Basketball" }));
    }

    [Test]
    public void RawRecordings_OfReactAdminAntDesignProAndKendo_GiveFlowsThatReplay()
    {
        // react-admin's demo logs in as demo, with the password demo: typed live, though no file of the recorder keeps it.
        var customers = Read("ReactAdminCustomers");
        foreach (var typed in customers.Actions.Where(action => action.Target?.Type == "password")) (typed.Value, typed.Raw) = ("demo", "demo");
        var customersFlow = FlowCodeGenerator.Generate(customers, "CustomersFilterEdit", "Flows");
        var widgets = FlowCodeGenerator.Generate(Read("KendoWidgets"), "KendoWidgets", "Flows");
        var rules = FlowCodeGenerator.Generate(Read("AntdProTable"), "AntdProTable", "Flows");
        var poster = FlowCodeGenerator.Generate(Read("ReactAdminPosters"), "Posters", "Flows");
        var steps = FlowCodeGenerator.Generate(Read("AntdProStepForm"), "AntdProStepForm", "Flows");
        var scheduler = FlowCodeGenerator.Generate(Read("KendoScheduler"), "KendoScheduler", "Flows");
        var search = FlowCodeGenerator.Generate(Read("AntdProSearch"), "AntdProSearch", "Flows");

        Assert.Multiple(() =>
        {
            Assert.That(customersFlow.Code, Does.Contain("Locator(\"[name='password']\").FillAsync(Configuration.Password)")
                .And.Not.Contain("[name='password']\").FillAsync(Configuration.Username)"),
                "a password the user name equals is the password's setting");
            Assert.That(customersFlow.Yaml, Does.Contain("Username: 'demo'").And.Contain("Password: '${PASSWORD}'").And.Not.Contain("Password: 'demo'"),
                "the YAML reads it from the environment");
            Assert.That((customers.LogsIn, Read("ReactAdminOrders").LogsIn), Is.EqualTo((true, false)),
                "a recording that logs in first is replayed, tested and continued signed out, as it was recorded");
            Assert.That(Regex.Count(widgets.Code, @"page\.Dialog \+= AcceptBrowserDialog;"), Is.EqualTo(1),
                "two deletes each confirmed in the browser: one handler accepts both, as a second would accept the same dialog again");
            Assert.That(widgets.Code, Does.Contain("page.Dialog -= AcceptBrowserDialog;").And.Not.Contain("browserDialog.AcceptAsync()"),
                "and the flow has it once however often it runs on the page");
            Assert.That(rules.Code, Does.Not.Contain("GetByRole(AriaRole.Row, new() { Name =").And.Not.Contain("5740000"),
                "a row is never found by all it says, its random counts and statuses with it");
            Assert.That(widgets.Code + scheduler.Code + steps.Code, Does.Not.Contain("_9b2b62df").And.Not.Contain("\\\\36 d9a47e9").And.Not.Contain("rc-tabs-0"),
                "no id made anew on each load or render: a GUID, one CSS escapes, one a library numbers");
            Assert.That(scheduler.Checks.Select(check => check.Message), Has.Some.Contains("only an id made anew on each load finds was left out"),
                "a click only such an id finds is left out, and said");
            Assert.That(steps.Code, Does.Contain("GetByRole(AriaRole.Button, new() { NameRegex = new Regex(\"^(?:loading )?Next$\") })")
                .And.Not.Contain("LoadingNext").And.Not.Contain("Name = \"loading Next\""),
                "a button found with or without the spinner it showed, its step named after what it says");
            Assert.That(search.Code, Does.Contain("NameRegex = new Regex(\"^(?:English)?English$\")").And.Not.Contain("\"EnglishEnglish\""),
                "a menu's item whose flag says its name again");
            Assert.That(rules.Code, Does.Not.Contain("{Configuration.Name}9").And.Not.Contain("GetByTitle(\"[object Object]\")"),
                "a setting only where the text is its value, and never a title no person wrote");
            Assert.That(poster.Checks.Select(check => check.Message), Has.Some.Contains("may change from run to run (\"Black & White 14.4x17.88, US$15.57\")"),
                "a name with a price a recording of an older recorder kept is one to check by hand");
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

        // Presses on one element, moves to the other in steps, and lets go there.
        public async Task DragToAsync(ILocator from, ILocator to)
        {
            await MoveAsync(from);
            await page.Mouse.DownAsync();
            var box = await to.BoundingBoxAsync() ?? throw new InvalidOperationException("The target does not show.");
            await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2, new() { Steps = 8 });
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
