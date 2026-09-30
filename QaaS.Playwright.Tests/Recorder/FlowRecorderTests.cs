using QaaS.Playwright.Recorder;

namespace QaaS.Playwright.Tests.Recorder;

[TestFixture]
public class FlowRecorderTests
{
    private DirectoryInfo _folder = null!;

    [SetUp]
    public void CreateFolder() => _folder = Directory.CreateTempSubdirectory("qaas-recorder-");

    [TearDown]
    public void DeleteFolder() => _folder.Delete(recursive: true);

    [Test]
    public void Save_AgainAndAgain_NeverOverwritesAndNeverAddsASecondClass()
    {
        var request = new RecordRequest("LoginFlow", "https://app.test/login", _folder.FullName);
        string[] actions = ["""await page.GetByLabel("User").FillAsync("admin");"""];

        var saved = Enumerable.Range(0, 3).Select(_ => FlowRecorder.Save(request, actions)).Select(Path.GetFileName);

        Assert.That(saved, Is.EqualTo(new[] { "LoginFlow.cs", "LoginFlow.recorded.txt", "LoginFlow.recorded-2.txt" }),
            "a re-recording is not a .cs file, which would declare LoginFlow twice and break the build");
    }

    [Test]
    public void Saved_PrintsTheYamlWithTheUrlTheRecordingStartedOn()
    {
        // The recorded flow starts without its first GotoAsync, so BaseUrl must take the page where recording began.
        const string url = "https://app.test/login?tenant=qa#sign-in";
        var output = new StringWriter();
        var console = Console.Out;
        Console.SetOut(output);
        try
        {
            ConsoleUi.Saved(new RecordRequest("LoginFlow", url, "Flows"), "Flows/LoginFlow.cs", 1);
        }
        finally
        {
            Console.SetOut(console);
        }

        Assert.That(output.ToString(), Does.Contain($"BaseUrl: {url}"));
    }
}
