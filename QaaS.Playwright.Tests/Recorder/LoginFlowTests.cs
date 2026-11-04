using Microsoft.Playwright;
using QaaS.Playwright.Browser;
using QaaS.Playwright.Flows;
using QaaS.Playwright.Recorder;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Login;
using QaaS.Playwright.Recorder.Setup;
using QaaS.Playwright.Recorder.TestRun;
using QaaS.Playwright.Tests.EndToEnd;
using static Microsoft.Playwright.Assertions;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>
/// A recording that starts after a login flow of the person's tests: found by itself in the test project and the DLLs
/// beside its build, as a shared package's flow is, with its values from the tests' YAML; or one that hangs, which
/// the person sees going and stops, to log in by hand.
/// </summary>
[TestFixture]
[Category("EndToEnd")]
public class LoginFlowTests
{
    [Test]
    public async Task TheSetupFlowOfASharedPackage_IsFound_WithItsValues_FoundAgainOnceRemembered_AndRunsFromTheProjectsDll()
    {
        var folder = Directory.CreateTempSubdirectory("qaas-recorder-found-").FullName;
        var logins = RememberedLogin.FilePath;
        RememberedLogin.FilePath = Path.Combine(folder, "recorder-logins.json");
        try
        {
            // A shared package of the team, with their login, and a test project that only references it.
            var shared = Path.Combine(folder, "Team.Shared");
            Directory.CreateDirectory(shared);
            await FlowCompiler.CreateProjectAsync(shared, "Team.Shared");
            await File.WriteAllTextAsync(Path.Combine(FlowCompiler.FlowsFolder(shared), "TeamSignInFlow.cs"), """
                using Microsoft.Playwright;
                using QaaS.Playwright;

                namespace GeneratedFlows.Flows;

                public sealed record TeamSignInConfig
                {
                    public string User { get; init; } = "";

                    public string Password { get; init; } = "";

                    public string Path { get; init; } = "/login";
                }

                public sealed class TeamSignInFlow : BasePlaywrightFlow<TeamSignInConfig>
                {
                    public override Task RunAsync(IPage page) => Task.CompletedTask;
                }

                public sealed class OpenTeamHomeFlow : BasePlaywrightFlow<TeamSignInConfig>
                {
                    public override Task RunAsync(IPage page) => Task.CompletedTask;
                }
                """);
            var tests = Path.Combine(folder, "Team.Tests");
            Directory.CreateDirectory(Path.Combine(tests, "Tests"));
            await File.WriteAllTextAsync(Path.Combine(tests, "Team.Tests.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Team.Shared/Team.Shared.csproj" />
                  </ItemGroup>
                </Project>
                """);
            var (built, log) = await FlowBuild.DotnetAsync(tests, ["build", "--nologo"], CancellationToken.None);
            Assert.That(built, Is.Zero, log);
            // Two tests log in with it; one opens the home page first; a copy in bin, and obj, is no test.
            const string smoke = """
                ProbeConfiguration:
                  SetupFlows: [TeamSignInFlow]
                  FlowConfiguration:
                    TeamSignInFlow:
                      User: ${QAAS_RECORDER_TEST_USER ?? alice}
                """;
            await File.WriteAllTextAsync(Path.Combine(tests, "Tests", "Smoke.qaas.yaml"), smoke);
            await File.WriteAllTextAsync(Path.Combine(tests, "Tests", "Orders.qaas.yaml"), "SetupFlows:\n  - TeamSignInFlow\n");
            foreach (var other in (string[])[Path.Combine("Tests", "Home.qaas.yaml"), Path.Combine("bin", "Home.yaml"), Path.Combine("obj", "Home.yaml")])
                await File.WriteAllTextAsync(Path.Combine(tests, other), "SetupFlows: [OpenTeamHomeFlow]\n");

            // The project's own DLL, which has no flow, beside the package's.
            FlowAssemblyContext.Load(Path.Combine(tests, "bin", "Debug", "net10.0", "Team.Tests.dll"));
            var flow = FlowDiscovery.Resolve("TeamSignInFlow").GetType();

            var look = LoginFinder.Look(Path.Combine(tests, "Flows"), tests);
            var found = LoginFinder.LoginOf(look.Suggested!, look.Discovered!);
            // Remembered as the start screen remembers it on Start: the next time, the same flow is found again.
            RememberedLogin.Remember(tests, found);
            var next = LoginFinder.Look(Path.Combine(tests, "Flows"), tests);
            var again = LoginFinder.LoginOf(next.Discovered!.Again!, next.Discovered);
            var values = LoginValues.Of(flow, found?.Settings);
            Assert.Multiple(() =>
            {
                Assert.That(found?.Flow, Is.EqualTo("TeamSignInFlow"), "the setup flow most tests run");
                Assert.That(found?.Assembly, Is.EqualTo(Path.Combine(tests, "bin", "Debug", "net10.0", "Team.Shared.dll")));
                Assert.That(found?.Settings, Is.EqualTo(Path.Combine(tests, "Tests", "Smoke.qaas.yaml")));
                Assert.That(found?.Found, Is.True);
                Assert.That(again, Is.EqualTo(found));
                Assert.That(LoginSettings.Read(found?.Settings, "TeamSignInFlow")["TeamSignInFlow:User"], Is.EqualTo("alice"),
                    "its placeholders resolved as QaaS resolves them");
                Assert.That(values.Given, Is.EqualTo(new[] { "User" }));
                Assert.That(values.Missing, Is.EqualTo(new[] { "Password" }), "text that is empty unless set");
                Assert.That(LoginSettings.Read(Path.Combine(tests, "Tests", "Orders.qaas.yaml"), "TeamSignInFlow").GetChildren(), Is.Empty,
                    "a YAML of no FlowConfiguration gives the flow its defaults");
            });
        }
        finally
        {
            RememberedLogin.FilePath = logins;
            Directory.Delete(folder, recursive: true);
        }
    }

    [Test]
    public async Task ALoginThatPlantsATokenFirst_RunsBeforeAnyPageLoads_WithTheTestsBaseUrl_ThenTheRecordingStartsDeepIn()
    {
        if (ChromeExecutable.Find() is null) Assert.Ignore("Google Chrome is not installed.");
        using var site = TestSite.Start();
        var folder = Directory.CreateTempSubdirectory("qaas-recorder-gate-").FullName;
        // The tests' probe gives the login its BaseUrl, the app's root; the recording starts on a page deep in it.
        var yaml = Path.Combine(folder, "Smoke.qaas.yaml");
        await File.WriteAllTextAsync(yaml, $"""
            ProbeConfiguration:
              BaseUrl: '{site.Url}/gate'
              SetupFlows: [GatedLogInFlow]
              FlowConfiguration:
                GatedLogInFlow:
                  User: carol
            """);
        var login = new LoginFlow("GatedLogInFlow", typeof(GatedLogInFlow).Assembly.Location, yaml);
        var start = $"{site.Url}/gate/orders/42";
        var (baseUrl, from) = login.BaseUrlFor(start);
        var session = await RecordingSession.StartAsync(new RecordingOptions(start)
        {
            LoginFlow = login.Flow, LoginBaseUrl = baseUrl, LoadStorageStatePath = null, SaveStorageStatePath = null,
            BrowserArgs = ["--headless=new"],
        }, _ => { });
        Recording recording;
        LoginEnd? end;
        string startedOn, user;
        try
        {
            end = await session.LogInAsync((page, stopped) => LoginRunner.RunAsync(login, page, baseUrl, stopped, from));
            startedOn = session.Page.Url;
            user = await session.Page.Locator("#user").InnerTextAsync();
            // Later pages load with the token the login planted, and are recorded.
            await session.Page.GetByRole(AriaRole.Link, new() { Name = "Home" }).ClickAsync();
            await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Home" })).ToBeVisibleAsync();
            await session.Page.GetByRole(AriaRole.Link, new() { Name = "Order 42" }).ClickAsync();
            await session.Page.GetByRole(AriaRole.Button, new() { Name = "Ship" }).ClickAsync();
            await Expect(session.Page.Locator("#done")).ToHaveTextAsync("shipped");
        }
        finally
        {
            recording = await session.StopAsync();
            Directory.Delete(folder, recursive: true);
        }

        Assert.Multiple(() =>
        {
            Assert.That((baseUrl, from), Is.EqualTo(($"{site.Url}/gate", yaml)), "the BaseUrl of the probe that runs it");
            Assert.That(end, Is.Null);
            Assert.That(site.Blocked, Is.Zero, "no page loaded before the login planted its token");
            Assert.That(site.Logins, Is.EqualTo(1));
            Assert.That(startedOn, Is.EqualTo(start), "the recording starts on the URL given, after the login");
            Assert.That(user, Is.EqualTo("carol"));
            Assert.That(recording.StartUrl, Is.EqualTo(start));
            Assert.That(recording.Actions.Select(action => action.Kind), Is.EqualTo(new[] { "click", "click", "click" }),
                "the capture script records on the pages the login's script runs on, and nothing the login did");
            Assert.That(LoginRunner.WhereItWas("GatedLogInFlow", new TimeoutException("Timeout 15000ms exceeded."), $"{site.Url}/x/login",
                    baseUrl, from), Does.StartWith($"GatedLogInFlow waited 15 s on {site.Url}/x/login: was BaseUrl right? It was given " +
                                                   $"BaseUrl {site.Url}/gate, from {yaml}."));
        });
    }

    [Test]
    public async Task ALoginThatHangs_ShowsHowItGoes_AndStops_ForTheLoginByHand()
    {
        if (ChromeExecutable.Find() is null) Assert.Ignore("Google Chrome is not installed.");
        using var site = TestSite.Start();
        var login = new LoginFlow("HangingLogInFlow", typeof(HangingLogInFlow).Assembly.Location, null);
        var progress = new List<LoginProgress>();
        var session = await RecordingSession.StartAsync(new RecordingOptions(site.Url)
        {
            LoginFlow = login.Flow, LoadStorageStatePath = null, SaveStorageStatePath = null, BrowserArgs = ["--headless=new"],
        }, news =>
        {
            if (news is LoginProgress going) lock (progress) progress.Add(going);
        });
        Recording recording;
        LoginEnd? end;
        IPage hung;
        try
        {
            hung = session.Page;
            var loggingIn = session.LogInAsync((page, stopped) => LoginRunner.RunAsync(login, page, site.Url, stopped));
            for (var waited = 0; waited < 100 && session.LoginProgress?.Last is null; waited++) await Task.Delay(100);
            session.LogInByHand();
            end = await loggingIn;
            // The person logs in in the page put in its place, and then records.
            await session.TogglePauseAsync();
            await session.Page.GetByLabel("Username").FillAsync("bob");
            await session.Page.GetByRole(AriaRole.Button, new() { Name = "Log in" }).ClickAsync();
            await Expect(session.Page.Locator("#user")).ToHaveTextAsync("bob");
        }
        finally
        {
            recording = await session.StopAsync();
        }

        LoginProgress last;
        lock (progress) last = progress[^1];
        Assert.Multiple(() =>
        {
            Assert.That(last.Text, Does.StartWith("Logging in with HangingLogInFlow… ").And.EndWith(" s, on /login"));
            Assert.That(last.Last, Is.EqualTo("typed in “Username”"), "what it did last, never the value");
            Assert.That(end, Is.EqualTo(new LoginEnd("You stopped HangingLogInFlow.", ByHand: true)));
            Assert.That(hung.IsClosed, Is.True, "its page is closed, so whatever it waited for fails, and it does nothing more");
            Assert.That(recording.Actions.Select(action => (action.Kind, action.Page)),
                Is.EqualTo(new[] { ("fill", "page"), ("click", "page") }), "recorded on the page put in its place, as the first page");
        });
    }

    [Test]
    public async Task Recording_StartsLoggedIn_AndRecordsNothingTheLoginFlowDid()
    {
        if (ChromeExecutable.Find() is null) Assert.Ignore("Google Chrome is not installed.");
        using var site = TestSite.Start();
        var folder = Directory.CreateTempSubdirectory("qaas-recorder-login-").FullName;
        var settings = Path.Combine(folder, "LogInFlow.flow.yaml");
        await File.WriteAllTextAsync(settings, "FlowConfiguration:\n  LogInFlow:\n    User: 'alice'\n");
        // As the start screen gives it: the flow by its class name, in the test project's built assembly, with its values.
        var request = SetupRules.For("submit-order", site.Url) with { Login = new LoginFlow("LogInFlow", typeof(LogInFlow).Assembly.Location, settings) };
        var session = await RecordingSession.StartAsync(new RecordingOptions(site.Url)
        {
            LoginFlow = request.Login!.Flow, LoadStorageStatePath = null, SaveStorageStatePath = null,
            BrowserArgs = ["--headless=new"],
        }, _ => { });
        Recording recording;
        LoginEnd? failure;
        try
        {
            failure = await session.LogInAsync((page, _) => LoginRunner.RunAsync(request.Login, page, request.Url));
            var page = session.Page;
            await Expect(page.Locator("#user")).ToHaveTextAsync("alice");
            await page.GotoAsync($"{site.Url}/order");
            await page.GetByTestId("submit-order").ClickAsync();
            await Expect(page.Locator("#result")).ToHaveTextAsync("submitted");
        }
        finally
        {
            recording = await session.StopAsync();
            Directory.Delete(folder, recursive: true);
        }

        var code = FlowCodeGenerator.Generate(recording, request.ClassName, "Flows").Code;
        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.Null);
            Assert.That(site.Logins, Is.EqualTo(1));
            Assert.That(recording.Actions.Select(action => action.Kind), Is.EqualTo(new[] { "navigate", "click" }),
                "the login's typing, click and pages are not recorded");
            Assert.That(code, Does.Contain($"after <c>LogInFlow</c>"));
            Assert.That(code, Does.Contain("""await page.GotoAsync($"{BaseUrl}/order");"""));
        });
    }
}
