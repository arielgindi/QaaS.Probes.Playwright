using QaaS.Playwright.Flows;
using QaaS.Playwright.Recorder;
using QaaS.Playwright.Recorder.Login;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>
/// The recorder finds the login flow of the tests as QaaS finds a setup flow when they run: from a folder above the
/// test project, through a custom output path, in a referenced project or package, and says plainly why it did not,
/// e.g. a flow built against another QaaS.Playwright. Its questions cannot be answered with the flow being recorded.
/// </summary>
[TestFixture]
public class LoginDiscoveryTests
{
    private TextReader _input = null!;
    private TextWriter _output = null!;
    private string _logins = null!;
    private string _folder = null!;

    [SetUp]
    public void Remember()
    {
        (_input, _output, _logins) = (Console.In, Console.Out, RememberedLogin.FilePath);
        _folder = Directory.CreateTempSubdirectory("qaas-recorder-discovery-").FullName;
        RememberedLogin.FilePath = Path.Combine(_folder, "recorder-logins.json");
    }

    [TearDown]
    public void Restore()
    {
        Console.SetIn(_input);
        Console.SetOut(_output);
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

        var why = Said(() => LoginFinder.Explain(Path.Combine(repo, "Flows"), repo));
        // No .csproj above the root: one below it, in the solution, that uses QaaS; built where MSBuild says.
        var request = new RecordRequest("NewOrder", "https://acme.test/app/orders/42", Path.Combine(repo, "Flows"));
        LoginFlow? found = null;
        var offered = Said(() => found = LoginFinder.Offer(request, repo)?.Login, "\n");

        // Never silent: no project at all, and a "no" said before.
        var empty = Directory.CreateDirectory(Path.Combine(_folder, "empty")).FullName;
        var none = Said(() => LoginFinder.Offer(request with { OutputDir = Path.Combine(empty, "Flows") }, empty));
        var inTests = request with { OutputDir = Path.Combine(tests, "Flows") };
        Said(() => LoginFinder.Offer(inTests, tests), "n\n");
        var saidNo = Said(() => LoginFinder.Offer(inTests, tests));

        // Asked by hand: the flow being recorded is refused, the class name checked as QaaS finds it, and neither its
        // DLL nor its values asked, as they were found.
        RecordRequest? asked = null;
        var questions = Said(() => asked = RecordRequest.Ask(find: _ => null, discover: LoginFinder.Discover),
            $"https://acme.test/app/orders/42\nnew-order\n{Path.Combine(tests, "Flows")}\ny\nNewOrder\nNoSuchFlow\nacmesigninflow\n");

        Assert.Multiple(() =>
        {
            Assert.That(why, Does.Contain($"No .csproj in {repo} or a folder above it: looking up to 3 folders below it"));
            Assert.That(why, Does.Contain("Acme.Tests.csproj (in Repo.slnx): uses QaaS: a YAML file of it runs SetupFlows"));
            Assert.That(why, Does.Contain("Acme.Tools.csproj (in Repo.slnx): not taken, as it does not use QaaS"));
            Assert.That(why, Does.Contain($"Taken: {Path.Combine(built, "Acme.Tests.dll")}"), "where Directory.Build.props builds it");
            Assert.That(why, Does.Contain("Acme.Shared.dll (a dependency of it, built against QaaS.Playwright"));
            Assert.That(why, Does.Contain("AcmeSignInFlow (Acme.Shared.dll): taken: the setup flow of 1 test file"));
            Assert.That(why, Does.Contain("OpenAcmeHomeFlow (Acme.Shared.dll): not taken: no test runs it first"));
            Assert.That(why, Does.Contain("Result: recording here offers to log in first with AcmeSignInFlow"));

            Assert.That(offered, Does.Contain($"Your tests log in with AcmeSignInFlow, from {Path.Combine(built, "Acme.Shared.dll")}"));
            Assert.That(found, Is.EqualTo(new LoginFlow("AcmeSignInFlow", Path.Combine(built, "Acme.Shared.dll"), smoke, Found: true,
                Path.Combine(tests, "Acme.Tests.csproj"), smoke)));
            Assert.That(found?.BaseUrlFor(request.Url), Is.EqualTo(("https://acme.test/app", smoke)),
                "the BaseUrl of the probe that runs it, its placeholder's default, not the URL recorded on");
            Assert.That(new LoginFlow("AcmeSignInFlow", "", null).BaseUrlFor(request.Url), Is.EqualTo(("https://acme.test", (string?)null)),
                "with none, the address alone");

            Assert.That(none.Trim(), Is.EqualTo($"No login flow: no test project in {empty}, above it or up to 3 folders below it. " +
                                                "To log in first, run the recorder in your test project's folder, or give --login " +
                                                "<flow>; qaas-playwright --login-why shows the search."));
            Assert.That(saidNo.Trim(), Is.EqualTo($"Not logging in here (you said no on {DateTime.Now:yyyy-MM-dd}). " +
                                                  "--login AcmeSignInFlow to log in, --no-login to ask again."));

            Assert.That(asked?.Login?.Flow, Is.EqualTo("AcmeSignInFlow"));
            Assert.That(asked?.Login?.Settings, Is.EqualTo(smoke));
            Assert.That(questions, Does.Contain("Do your tests log in first with a flow, e.g. LoginFlow?"));
            Assert.That(questions, Does.Contain("(y/N, Enter = no)"));
            Assert.That(questions, Does.Contain("NewOrder is the flow you are recording now, so it cannot log you in first."));
            Assert.That(questions, Does.Contain("There is no flow named NoSuchFlow in your tests' build"));
            Assert.That(questions, Does.Not.Contain("The DLL that has").And.Not.Contain("Values AcmeSignInFlow needs"));
        });
    }

    [Test]
    public async Task ALoginFromALocalPackage_IsFoundInItsPackageFolder_AndOneBuiltAgainstAnotherQaaSPlaywright_IsSaidPlainly()
    {
        var pkg = Path.Combine(await LoginFixtures.BuildAsync(), "pkg");
        var request = new RecordRequest("NewOrder", "https://acme.test/orders", Path.Combine(pkg, "Flows"));

        // Two test projects below: asked once which, then remembered.
        var first = Said(() => Assert.That(LoginFinder.Offer(request, pkg), Is.Null), "1\n");
        var again = Said(() => Assert.That(LoginFinder.Offer(request, pkg), Is.Null));
        var (legacy, legacyWhy) = LoginFinder.Named("LegacyLoginFlow", Path.Combine(pkg, "Acme.LegacyTests", "Flows"));
        var refused = Assert.Throws<ArgumentException>(() => RecordRequest.Parse(["record", "new-order", "https://acme.test", "--login", "LegacyLoginFlow"],
            find: (flow, _) => LoginFinder.Named(flow, Path.Combine(pkg, "Acme.LegacyTests", "Flows"))));

        // The package's DLL is not beside the library's build: its .deps.json says where it is.
        var portalTests = Path.Combine(pkg, "Acme.PortalTests");
        var (portal, _) = LoginFinder.Named("portalloginflow", Path.Combine(portalTests, "Flows"));
        FlowAssemblyContext.Load(portal!.Assembly);
        var runs = FlowDiscovery.Resolve("PortalLoginFlow").GetType();

        var plainly = $"LegacyLoginFlow (in Acme.Legacy.dll) was built against QaaS.Playwright 1.15.1, and the recorder runs " +
                      $"{FlowCheck.RecorderVersion}: it can't load it (BrowserDefaults.RemoteUrl is gone). Update that package to " +
                      $"QaaS.Playwright {typeof(IPlaywrightFlow).Assembly.GetName().Version!.ToString(2)}.x";
        Assert.Multiple(() =>
        {
            Assert.That(first, Does.Contain("2 test projects here use QaaS."));
            Assert.That(first, Does.Contain("1  Acme.LegacyTests").And.Contain("2  Acme.PortalTests"));
            Assert.That(first, Does.Contain(plainly));
            Assert.That(again, Does.Not.Contain("test projects here use QaaS").And.Contain(plainly), "the project chosen is remembered");
            Assert.That(legacy, Is.Null);
            Assert.That(legacyWhy, Does.StartWith(plainly));
            Assert.That(refused?.Message, Does.StartWith(plainly), "--login alone says it too");
            Assert.That(portal.Flow, Is.EqualTo("PortalLoginFlow"), "found by its name, case-insensitively, as QaaS finds it");
            Assert.That(portal.Assembly, Is.EqualTo(Path.Combine(portalTests, "bin", "Debug", "net10.0", "Acme.PortalTests.dll")));
            Assert.That(runs.Assembly.Location, Does.StartWith(Path.Combine(pkg, "packages", "acme.portal", "1.0.0")));
        });
    }

    // What the recorder printed while it ran, given the lines of input; a question that has no answer left fails it.
    private static string Said(Action run, string input = "")
    {
        var output = new StringWriter();
        Console.SetOut(output);
        Console.SetIn(new StringReader(input));
        run();
        return Unwrapped(output.ToString());
    }

    // The console wraps long lines: a sentence is checked whole.
    private static string Unwrapped(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\n\s+(?=\S)", " ").Replace('\n', ' ');
}
