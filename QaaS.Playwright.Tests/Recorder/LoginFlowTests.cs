using QaaS.Playwright.Browser;
using QaaS.Playwright.Recorder;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Login;
using QaaS.Playwright.Tests.EndToEnd;
using static Microsoft.Playwright.Assertions;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>A recording that starts after a login flow of the person's tests, found in their built assembly.</summary>
[TestFixture]
[Category("EndToEnd")]
public class LoginFlowTests
{
    [Test]
    public async Task Recording_StartsLoggedIn_AndRecordsNothingTheLoginFlowDid()
    {
        if (ChromeExecutable.Find() is null) Assert.Ignore("Google Chrome is not installed.");
        using var site = TestSite.Start();
        var folder = Directory.CreateTempSubdirectory("qaas-recorder-login-").FullName;
        var settings = Path.Combine(folder, "LogInFlow.flow.yaml");
        await File.WriteAllTextAsync(settings, "FlowConfiguration:\n  LogInFlow:\n    User: 'alice'\n");
        // As a person gives it: the flow by its class name, in the test project's built assembly.
        var request = RecordRequest.Parse(["record", "submit-order", site.Url, "--login", "LogInFlow",
            "--login-assembly", typeof(LogInFlow).Assembly.Location, "--login-settings", settings]);
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
