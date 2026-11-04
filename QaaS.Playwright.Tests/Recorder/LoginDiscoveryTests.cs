using QaaS.Playwright.Flows;
using QaaS.Playwright.Recorder.Login;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>
/// The recorder finds the login flow of the tests as QaaS finds a setup flow when they run: from a folder above the
/// test project, through a custom output path, in a referenced project or package, and says plainly why it did not,
/// e.g. a flow built against another QaaS.Playwright. It asks nothing: the start screen shows what it found.
/// </summary>
[TestFixture]
public class LoginDiscoveryTests
{
    private string _logins = null!;
    private string _folder = null!;

    [SetUp]
    public void Remember()
    {
        _logins = RememberedLogin.FilePath;
        _folder = Directory.CreateTempSubdirectory("qaas-recorder-discovery-").FullName;
        RememberedLogin.FilePath = Path.Combine(_folder, "recorder-logins.json");
    }

    [TearDown]
    public void Restore()
    {
        RememberedLogin.FilePath = _logins;
        Directory.Delete(_folder, recursive: true);
    }

    [Test]
    public async Task FromTheRepositorysRoot_TheTestProjectBelow_ItsBuildElsewhere_AndItsSharedLogin_AreFound_AndNothingIsSilent()
    {
        var repo = Path.Combine(await LoginFixtures.BuildAsync(), "repo");
        var tests = Path.Combine(repo, "tests", "Acme.Tests");
        var built = Path.Combine(repo, "out", "Acme.Tests", "Debug", "net10.0");
        var smoke = Path.Combine(tests, "Tests", "Smoke.qaas.yaml");

        var why = Said(LoginFinder.Explained(Path.Combine(repo, "Flows"), repo));
        // No .csproj above the root: one below it, in the solution, that uses QaaS; built where MSBuild says.
        var look = LoginFinder.Look(Path.Combine(repo, "Flows"), repo);
        var found = LoginFinder.LoginOf(look.Suggested!, look.Discovered!);

        // Never silent: no project at all, and a no said before.
        var empty = Directory.CreateDirectory(Path.Combine(_folder, "empty")).FullName;
        var none = LoginFinder.Look(Path.Combine(empty, "Flows"), empty);
        RememberedLogin.Decline(tests, "AcmeSignInFlow");
        var saidNo = Said(LoginFinder.Explained(Path.Combine(tests, "Flows"), tests));

        Assert.Multiple(() =>
        {
            Assert.That(why, Does.Contain($"No .csproj in {repo} or a folder above it: looking up to 3 folders below it"));
            Assert.That(why, Does.Contain("Acme.Tests.csproj (in Repo.slnx): uses QaaS: a YAML file of it runs SetupFlows"));
            Assert.That(why, Does.Contain("Acme.Tools.csproj (in Repo.slnx): not taken, as it does not use QaaS"));
            Assert.That(why, Does.Contain($"Taken: {Path.Combine(built, "Acme.Tests.dll")}"), "where Directory.Build.props builds it");
            Assert.That(why, Does.Contain("Acme.Shared.dll (a dependency of it, built against QaaS.Playwright"));
            Assert.That(why, Does.Contain("AcmeSignInFlow (Acme.Shared.dll): taken: the setup flow of 1 test file"));
            Assert.That(why, Does.Contain("OpenAcmeHomeFlow (Acme.Shared.dll): not taken: no test runs it first"));
            Assert.That(why, Does.Contain("Result: recording here logs in first with AcmeSignInFlow"));

            Assert.That(found, Is.EqualTo(new LoginFlow("AcmeSignInFlow", Path.Combine(built, "Acme.Shared.dll"), smoke, Found: true,
                Path.Combine(tests, "Acme.Tests.csproj"), smoke)));
            Assert.That(found.BaseUrlFor("https://acme.test/app/orders/42"), Is.EqualTo(("https://acme.test/app", smoke)),
                "the BaseUrl of the probe that runs it, its placeholder's default, not the URL recorded on");
            Assert.That(new LoginFlow("AcmeSignInFlow", "", null).BaseUrlFor("https://acme.test/app/orders/42"), Is.EqualTo(("https://acme.test", (string?)null)),
                "with none, the address alone");

            Assert.That(none.Discovered, Is.Null);
            Assert.That(none.Search.Why, Is.EqualTo($"no test project in {empty}, above it or up to 3 folders below it"));
            Assert.That(saidNo, Does.Contain($"Remembered here: Not logging in here (Log in first turned off on {DateTime.Now:yyyy-MM-dd})."));
        });
    }

    [Test]
    public async Task ALoginFromALocalPackage_IsFoundInItsPackageFolder_AndOneBuiltAgainstAnotherQaaSPlaywright_IsSaidPlainly()
    {
        var pkg = Path.Combine(await LoginFixtures.BuildAsync(), "pkg");

        // Two test projects below: chosen once on the start screen, then remembered.
        var several = LoginFinder.Look(Path.Combine(pkg, "Flows"), pkg);
        RememberedLogin.RememberProject(pkg, several.Projects.Single(project => project.Name == "Acme.LegacyTests").ProjectFile);
        var chosen = LoginFinder.Look(Path.Combine(pkg, "Flows"), pkg);
        var legacy = LoginFinder.Look(Path.Combine(pkg, "Acme.LegacyTests", "Flows"), Path.Combine(pkg, "Acme.LegacyTests"));

        // The package's DLL is not beside the library's build: its .deps.json says where it is.
        var portalTests = Path.Combine(pkg, "Acme.PortalTests");
        var portalLook = LoginFinder.Look(Path.Combine(portalTests, "Flows"), portalTests);
        var portal = LoginFinder.LoginOf(portalLook.Discovered!.Flows.Single(flow => flow.Name.Equals("portalloginflow", StringComparison.OrdinalIgnoreCase)),
            portalLook.Discovered);
        FlowAssemblyContext.Load(portal.Assembly);
        var runs = FlowDiscovery.Resolve("PortalLoginFlow").GetType();

        var plainly = $"LegacyLoginFlow (in Acme.Legacy.dll) was built against QaaS.Playwright 1.15.1, and the recorder runs " +
                      $"{FlowCheck.RecorderVersion}: it can't load it (BrowserDefaults.RemoteUrl is gone). Update that package to " +
                      $"QaaS.Playwright {typeof(IPlaywrightFlow).Assembly.GetName().Version!.ToString(2)}.x";
        Assert.Multiple(() =>
        {
            Assert.That(several.Projects.Select(project => project.Name), Is.EquivalentTo(new[] { "Acme.LegacyTests", "Acme.PortalTests" }));
            Assert.That(Said(several.Search), Does.Contain("2 of them use QaaS"));
            Assert.That(chosen.Projects, Is.Empty, "the project chosen is remembered");
            Assert.That(chosen.Discovered?.Problems, Has.Some.StartsWith(plainly));
            Assert.That(legacy.Discovered?.Problems, Has.Some.StartsWith(plainly));
            Assert.That(legacy.Suggested, Is.Null, "a login that cannot run is not offered");
            Assert.That(portal.Flow, Is.EqualTo("PortalLoginFlow"), "found by its name, case-insensitively, as QaaS finds it");
            Assert.That(portal.Assembly, Is.EqualTo(Path.Combine(portalTests, "bin", "Debug", "net10.0", "Acme.PortalTests.dll")));
            Assert.That(runs.Assembly.Location, Does.StartWith(Path.Combine(pkg, "packages", "acme.portal", "1.0.0")));
        });
    }

    // Every step of the search, as one text, a sentence checked whole.
    private static string Said(LoginSearch search) => string.Join(" ", search.Lines.Select(line => line.Text));
}
