using Microsoft.Playwright;
using QaaS.Playwright.Browser;
using QaaS.Playwright.Recorder;
using QaaS.Playwright.Recorder.Login;
using QaaS.Playwright.Recorder.Panel;
using QaaS.Playwright.Recorder.Setup;
using QaaS.Playwright.Tests.EndToEnd;
using static Microsoft.Playwright.Assertions;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>
/// The recorder as a person runs it, in its window, in a headless Chrome: the start screen on the project it was started
/// in, a name and a start page typed there, Start, a recording, Save, what was saved, and Record another; a recording
/// never saved, offered and saved without Chrome; and a recorder started where no project is, which asks for one.
/// </summary>
[TestFixture]
[Category("EndToEnd")]
public class SetupScreenTests
{
    private string _folder = null!;
    private (string Logins, string Projects, string Prefs) _was;

    [SetUp]
    public void UseFilesOfItsOwn()
    {
        if (ChromeExecutable.Find() is null) Assert.Ignore("Google Chrome is not installed.");
        _folder = Directory.CreateTempSubdirectory("qaas-setup-screen-").FullName;
        _was = (RememberedLogin.FilePath, ProjectSettings.FilePath, PanelPrefs.FilePath);
        RememberedLogin.FilePath = Path.Combine(_folder, "recorder-logins.json");
        ProjectSettings.FilePath = Path.Combine(_folder, "recorder-projects.json");
        PanelPrefs.FilePath = Path.Combine(_folder, "recorder-panel.json");
        Autosave.Folder = Path.Combine(_folder, "recordings");
    }

    [TearDown]
    public void PutThemBack()
    {
        if (_folder is null) return;
        (RememberedLogin.FilePath, ProjectSettings.FilePath, PanelPrefs.FilePath) = _was;
        Autosave.Folder = null!;
        Directory.Delete(_folder, recursive: true);
    }

    [Test]
    public async Task AFlowIsSetUp_Recorded_AndSaved_InTheWindow_ThenAnotherIsSetUpThere()
    {
        using var site = TestSite.Start();
        var project = Directory.CreateDirectory(Path.Combine(_folder, "helpdesk-tests")).FullName;
        await using var host = await RecorderApp.OpenAsync(new RecorderHostOptions { StartFolder = project, BrowserArgs = ["--headless=new"], KeepsLogin = false });
        var window = host.Window.Page;

        await Expect(window.GetByRole(AriaRole.Status, new() { Name = "Setup" })).ToContainTextAsync("Set up");
        await Expect(window.GetByTitle(project)).ToBeVisibleAsync();
        var start = window.GetByRole(AriaRole.Button, new() { Name = "Start recording" });
        await Expect(start).ToBeDisabledAsync();
        await window.GetByLabel("name").FillAsync("task");
        await window.GetByLabel("url").ClickAsync();
        await Expect(window.GetByRole(AriaRole.Alert).Filter(new() { HasText = "A flow cannot be named Task" })).ToBeVisibleAsync();
        await window.GetByLabel("name").FillAsync("new-ticket");
        await window.GetByLabel("url").FillAsync($"{site.Url}/helpdesk");
        await Expect(window.GetByText("→ NewTicket, Flows/NewTicket.cs")).ToBeVisibleAsync();
        await Expect(start).ToBeEnabledAsync();
        await start.ClickAsync();

        var request = await host.Setup.NextAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(request, Is.EqualTo(SetupRules.For("new-ticket", $"{site.Url}/helpdesk", Path.Combine(project, "Flows"))), "no login found here, so none runs first");
        var outcome = new RecordingOutcome();
        var recording = FlowRecorder.RecordAsync(request!, host.Recording, outcome);
        var app = await AppPageAsync(host);
        await Expect(window.GetByRole(AriaRole.Region, new() { Name = "Code" })).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await app.GetByTestId("new-ticket").ClickAsync();
        await Expect(window.GetByRole(AriaRole.Region, new() { Name = "Code" })).ToContainTextAsync("new-ticket");
        await window.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        Assert.That(await recording.WaitAsync(TimeSpan.FromSeconds(30)), Is.Zero);
        await host.Setup.EndedAsync(outcome);

        await Expect(window.GetByRole(AriaRole.Heading, new() { Name = "Saved NewTicket" })).ToBeVisibleAsync();
        await Expect(window.GetByText(Path.Combine("Flows", "NewTicket.cs"))).ToBeVisibleAsync();
        await window.GetByRole(AriaRole.Button, new() { Name = "Record another" }).ClickAsync();
        await Expect(window.GetByLabel("name")).ToHaveValueAsync("");
        await Expect(window.GetByLabel("url")).ToHaveValueAsync($"{site.Url}/helpdesk", new() { Timeout = 5000 });
        await window.GetByLabel("name").FillAsync("new-ticket");
        await Expect(window.GetByText("NewTicket.cs is there already").First).ToBeVisibleAsync();
        await window.GetByRole(AriaRole.Tab, new() { Name = "Continue a saved flow" }).ClickAsync();
        await Expect(window.GetByRole(AriaRole.Radiogroup, new() { Name = "The flow to continue" })).ToContainTextAsync("NewTicket");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(project, "Flows", "NewTicket.cs")), Is.True);
            Assert.That(outcome.Saved, Is.True);
            Assert.That(ProjectSettings.Of(project)?.Urls, Is.EqualTo(new[] { $"{site.Url}/helpdesk" }), "the start page remembered");
            Assert.That(host.Setup.State.Mode, Is.EqualTo(QaaS.Playwright.Recorder.State.Mode.Setup));
        });
    }

    [Test]
    public async Task ARecordingNeverSaved_IsOffered_AndSavedWithoutChrome()
    {
        var project = Directory.CreateDirectory(Path.Combine(_folder, "shop-tests")).FullName;
        var flows = Path.Combine(project, "Flows");
        var kept = Captured.Recording(Captured.Action("click", Captured.Element("button", "Buy", Captured.Role("button", "Buy"))));
        await new Autosave().WriteAsync(kept, new RecordRequest("BuyNow", kept.StartUrl, flows));
        await using var host = await RecorderApp.OpenAsync(new RecorderHostOptions { StartFolder = project, BrowserArgs = ["--headless=new"], KeepsLogin = false });
        var window = host.Window.Page;

        await Expect(window.GetByRole(AriaRole.Tab, new() { Name = "Recovered" })).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(window.GetByRole(AriaRole.Button, new() { Name = "BuyNow" })).ToContainTextAsync("1 action");
        await window.GetByRole(AriaRole.Button, new() { Name = "Save it now" }).ClickAsync();
        await Expect(window.GetByRole(AriaRole.Heading, new() { Name = "Saved BuyNow" })).ToBeVisibleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(flows, "BuyNow.cs")), Is.True);
            Assert.That(Autosave.NotSaved(), Is.Empty, "no longer offered");
            Assert.That(host.Setup.State.Mode, Is.EqualTo(QaaS.Playwright.Recorder.State.Mode.Done));
        });
    }

    [Test]
    public async Task StartedWhereNoProjectIs_TheScreenAsksForOne_AndItsFolderBrowserFindsIt()
    {
        var project = Directory.CreateDirectory(Path.Combine(_folder, "repo", "Shop.Tests")).FullName;
        await File.WriteAllTextAsync(Path.Combine(project, "Shop.Tests.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await using var host = await RecorderApp.OpenAsync(new RecorderHostOptions
        {
            StartFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), BrowserArgs = ["--headless=new"], KeepsLogin = false,
        });
        var window = host.Window.Page;

        await Expect(window.GetByText("No project yet")).ToBeVisibleAsync();
        await Expect(window.GetByRole(AriaRole.Button, new() { Name = "Start recording" })).ToBeDisabledAsync();
        await window.GetByRole(AriaRole.Button, new() { Name = "Browse…" }).ClickAsync();
        var browser = window.GetByRole(AriaRole.Dialog);
        var path = browser.GetByLabel("Folder");
        await Expect(path).Not.ToHaveValueAsync("");
        await path.FillAsync(Path.Combine(_folder, "repo"));
        await path.PressAsync("Enter");
        await Expect(browser).ToContainTextAsync("Test projects are below this folder", new() { Timeout = 15_000 });
        await Expect(browser.GetByRole(AriaRole.Button, new() { Name = "Shop.Tests" })).ToContainTextAsync("a project");
        await browser.GetByRole(AriaRole.Button, new() { Name = "Shop.Tests" }).ClickAsync();
        await Expect(browser).ToContainTextAsync("The test project Shop.Tests is here.");
        await browser.GetByRole(AriaRole.Button, new() { Name = "Use this folder" }).ClickAsync();
        await Expect(window.GetByTitle(project)).ToBeVisibleAsync();
        Assert.That(host.Setup.Project.Folder, Is.EqualTo(project));
    }

    // The recording's page: the one of the context that is not the window's.
    private static async Task<IPage> AppPageAsync(RecorderHost host)
    {
        for (var waited = 0; waited < 200; waited++)
        {
            if (host.Browser.Browser.Contexts.FirstOrDefault(context => context != host.Window.Page.Context && context.Pages.Count > 0) is { } app)
                return app.Pages[0];
            await Task.Delay(100);
        }

        throw new TimeoutException("The recording's page did not open.");
    }
}
