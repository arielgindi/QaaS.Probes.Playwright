using QaaS.Playwright.Recorder;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Login;
using QaaS.Playwright.Recorder.Setup;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>
/// What the start screen is shown, found without a question or a build: the login search's result and its every step,
/// the projects to choose among, the flows of an output folder with what each can be continued from, the values typed
/// for a login flow over its file's, and what is remembered of each project.
/// </summary>
[TestFixture]
public class SetupDiscoveryTests
{
    private string _folder = null!;
    private string _logins = null!;
    private string _projects = null!;

    [SetUp]
    public void UseFilesOfItsOwn()
    {
        _folder = Directory.CreateTempSubdirectory("qaas-setup-discovery-").FullName;
        (_logins, _projects) = (RememberedLogin.FilePath, ProjectSettings.FilePath);
        RememberedLogin.FilePath = Path.Combine(_folder, "recorder-logins.json");
        ProjectSettings.FilePath = Path.Combine(_folder, "recorder-projects.json");
        Autosave.Folder = Path.Combine(_folder, "recordings");
    }

    [TearDown]
    public void PutThemBack()
    {
        (RememberedLogin.FilePath, ProjectSettings.FilePath, Autosave.Folder) = (_logins, _projects, null!);
        Directory.Delete(_folder, recursive: true);
    }

    [Test]
    public async Task TheLoginSearch_FindsTheTestsLogin_FromAboveTheirProject_AsData_AndSaysEveryStep()
    {
        var repo = Path.Combine(await LoginFixtures.BuildAsync(), "repo");
        var built = Path.Combine(repo, "out", "Acme.Tests", "Debug", "net10.0");
        var look = LoginFinder.Look(Path.Combine(repo, "Flows"), repo);
        var why = string.Join("\n", LoginFinder.Explained(Path.Combine(repo, "Flows"), repo).Lines.Select(line => line.Text));
        var smoke = Path.Combine(repo, "tests", "Acme.Tests", "Tests", "Smoke.qaas.yaml");

        Assert.Multiple(() =>
        {
            Assert.That(look.Projects, Is.Empty, "one project uses QaaS, so none is to choose");
            Assert.That(look.Discovered?.Project?.Name, Is.EqualTo("Acme.Tests"));
            Assert.That(look.Suggested?.Name, Is.EqualTo("AcmeSignInFlow"));
            Assert.That(look.Discovered!.Flows.Select(flow => flow.Name), Is.SupersetOf(new[] { "AcmeSignInFlow", "OpenAcmeHomeFlow" }));
            Assert.That(LoginFinder.LoginOf(look.Suggested!, look.Discovered).Assembly, Is.EqualTo(Path.Combine(built, "Acme.Shared.dll")));
            Assert.That(LoginFinder.SettingsFilesOf(look.Discovered, "AcmeSignInFlow"), Is.EqualTo(new[] { smoke }));
            Assert.That(look.BuildCommand, Does.StartWith("dotnet build"));
            Assert.That(look.Discovered.Files.SelectMany(file => LoginSettings.BaseUrls(file.Yaml)), Does.Contain("https://acme.test/app"),
                "the probe's BaseUrl, its placeholder's default, for the start page's choices");
            Assert.That(why, Does.Contain("AcmeSignInFlow (Acme.Shared.dll): taken: the setup flow of 1 test file"));
            Assert.That(why, Does.Contain("Result: recording here logs in first with AcmeSignInFlow"));
        });
    }

    [Test]
    public async Task SeveralTestProjects_AreOfferedToChooseAmong_UntilOneIsChosen()
    {
        var pkg = Path.Combine(await LoginFixtures.BuildAsync(), "pkg");
        var several = LoginFinder.Look(Path.Combine(pkg, "Flows"), pkg);
        RememberedLogin.RememberProject(pkg, several.Projects.Single(project => project.Name == "Acme.PortalTests").ProjectFile);
        var chosen = LoginFinder.Look(Path.Combine(pkg, "Flows"), pkg);
        Assert.Multiple(() =>
        {
            Assert.That(several.Projects.Select(project => project.Name), Is.EquivalentTo(new[] { "Acme.LegacyTests", "Acme.PortalTests" }));
            Assert.That(several.Discovered, Is.Null);
            Assert.That(chosen.Projects, Is.Empty);
            Assert.That(chosen.Discovered?.Project?.Name, Is.EqualTo("Acme.PortalTests"));
            Assert.That(chosen.Suggested?.Name, Is.EqualTo("PortalLoginFlow"));
        });
    }

    [Test]
    public async Task ValuesTyped_ForALoginFlow_AreReadOverItsFile_AndNeverWritten()
    {
        var yaml = Path.Combine(_folder, "Smoke.qaas.yaml");
        await File.WriteAllTextAsync(yaml, "FlowConfiguration:\n  ShopLoginFlow:\n    User: alice\n");
        LoginSettings.TypeValues("ShopLoginFlow", new Dictionary<string, string> { ["Password"] = "typed-in-the-window", ["Empty"] = "" });
        try
        {
            var read = LoginSettings.Read(yaml, "ShopLoginFlow");
            Assert.Multiple(() =>
            {
                Assert.That(read["ShopLoginFlow:User"], Is.EqualTo("alice"));
                Assert.That(read["ShopLoginFlow:Password"], Is.EqualTo("typed-in-the-window"));
                Assert.That(read["ShopLoginFlow:Empty"], Is.Null, "an empty field gives nothing");
                Assert.That(LoginSettings.Read(null, "OtherFlow")["OtherFlow:Password"], Is.Null, "another flow's are its own");
                Assert.That(LoginSettings.Read(null, "shoploginflow")["shoploginflow:Password"], Is.EqualTo("typed-in-the-window"), "with no file");
            });
        }
        finally
        {
            LoginSettings.TypeValues("ShopLoginFlow", new Dictionary<string, string>());
        }
    }

    [Test]
    public void EachProjectsChoices_AreRemembered_TheLastUsedFirst_WithItsLatestPagesFirst()
    {
        var shop = Directory.CreateDirectory(Path.Combine(_folder, "shop")).FullName;
        var crm = Directory.CreateDirectory(Path.Combine(_folder, "crm")).FullName;
        ProjectSettings.Remember(new ProjectChoices { Folder = shop, OutputDir = "UiFlows", AutoChecks = false }, "https://shop.test/orders");
        ProjectSettings.Remember(ProjectSettings.Of(shop)!, "https://shop.test/admin");
        ProjectSettings.Remember(ProjectSettings.Of(shop)!, "https://shop.test/orders");
        ProjectSettings.Remember(new ProjectChoices { Folder = crm + Path.DirectorySeparatorChar, Tab = SetupTab.Continue });
        var stored = File.ReadAllText(ProjectSettings.FilePath);
        var recent = ProjectSettings.Recent();
        ProjectSettings.Forget(crm);
        Assert.Multiple(() =>
        {
            Assert.That(recent.Select(project => project.Folder), Is.EqualTo(new[] { crm, shop }));
            Assert.That(ProjectSettings.Of(shop)?.Urls, Is.EqualTo(new[] { "https://shop.test/orders", "https://shop.test/admin" }));
            Assert.That(ProjectSettings.Of(shop)?.OutputDir, Is.EqualTo("UiFlows"));
            Assert.That(ProjectSettings.Of(shop)?.AutoChecks, Is.False);
            Assert.That(recent[0].Tab, Is.EqualTo(SetupTab.Continue));
            Assert.That(stored, Does.Contain("\"tab\": \"Continue\""));
            Assert.That(ProjectSettings.Recent().Select(project => project.Folder), Is.EqualTo(new[] { shop }), "forgotten");
        });
    }

    [Test]
    public async Task AnOutputFoldersFlows_AreListedNewestFirst_EachWithWhatItCanBeContinuedFrom()
    {
        var flows = Directory.CreateDirectory(Path.Combine(_folder, "shop", "Flows")).FullName;
        await File.WriteAllTextAsync(Path.Combine(flows, "Checkout.cs"), "// recorded elsewhere");
        File.SetLastWriteTimeUtc(Path.Combine(flows, "Checkout.cs"), DateTime.UtcNow.AddDays(-1));
        await File.WriteAllTextAsync(Path.Combine(flows, "NewOrder.cs"), "// recorded here");
        await new Autosave().SavedAsync(new Recording("https://shop.test/orders", [Captured.Action("click", Captured.Element("button", "Buy", Captured.Role("button", "Buy")))]),
            new RecordRequest("NewOrder", "https://shop.test/orders", flows), new SavedFlow(Path.Combine(flows, "NewOrder.cs"), Path.Combine(flows, "NewOrder.flow.yaml")), "// recorded here");
        var listed = ProjectFlows.Of(flows);
        Assert.Multiple(() =>
        {
            Assert.That(listed.Select(flow => flow.ClassName), Is.EqualTo(new[] { "NewOrder", "Checkout" }));
            Assert.That(listed[0].Kept?.Recording.StartUrl, Is.EqualTo("https://shop.test/orders"));
            Assert.That(listed[0].Hash, Is.EqualTo(Autosave.Hash("// recorded here")));
            Assert.That(listed[1].Kept, Is.Null, "nothing kept to continue it from");
            Assert.That(ProjectFlows.Of(Path.Combine(_folder, "none")), Is.Empty);
        });
    }
}
