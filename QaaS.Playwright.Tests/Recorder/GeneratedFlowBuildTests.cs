using System.Runtime.CompilerServices;
using QaaS.Playwright.Recorder;
using QaaS.Playwright.Recorder.Capture;
using static QaaS.Playwright.Tests.Recorder.Captured;
using CapturedRecording = QaaS.Playwright.Recorder.Capture.Recording;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>
/// Every kind of flow the recorder writes must build in a test project as strict as a team's: the golden recordings,
/// and the steps that read the flow's own members (BaseUrl, settings) or none.
/// </summary>
[TestFixture]
[Category("EndToEnd")]
public class GeneratedFlowBuildTests
{
    private string _folder = null!;

    [SetUp]
    public void CreateFolder() => _folder = Directory.CreateTempSubdirectory("qaas-recorder-build-").FullName;

    [TearDown]
    public void DeleteFolder() => Directory.Delete(_folder, recursive: true);

    [Test]
    public async Task EveryGeneratedFlow_Builds_WithoutWarnings()
    {
        await FlowCompiler.CreateProjectAsync(_folder, "GeneratedFlows");
        var flows = FlowCompiler.FlowsFolder(_folder);
        foreach (var (name, recording) in Recordings())
        {
            var flow = FlowCodeGenerator.Generate(recording, name, ProjectNamespace.Of(flows));
            await File.WriteAllTextAsync(Path.Combine(flows, $"{name}.cs"), flow.Code);
        }

        await FlowCompiler.BuildAsync(_folder);
    }

    private static IEnumerable<(string Name, CapturedRecording Recording)> Recordings()
    {
        foreach (var path in Directory.GetFiles(GoldenFolder(), "*.recording.json"))
            yield return (Path.GetFileName(path)[..^".recording.json".Length], CapturedRecording.FromJson(File.ReadAllText(path)));

        var typed = Action("navigate", null);
        typed.Url = "https://app.test/start/appointments";
        yield return ("TypedAddressThenDialog", Recording(typed,
            Action("click", Element("button", "New", Role("button", "New")), effects: Opened("New"))));

        var otherSite = Action("navigate", null);
        otherSite.Url = "https://other.test/";
        yield return ("TypedAddressOnAnotherSite", Recording(otherSite));

        var tab = Action("newTab", null);
        tab.Page = "page1";
        tab.Url = "https://app.test/start/help";
        var search = Action("fill", Element("textbox", "Search", Css("name", "[name='q']", "q")), "x");
        search.Page = "page1";
        yield return ("TabOpenedByHand", Recording(tab, search));

        var confirm = Action("click", Element("button", "Delete", Role("button", "Delete")),
            effects: new CapturedEffect { Kind = "jsDialog", Text = "Delete it?", Page = "page" });
        yield return ("BrowserDialog", Recording(confirm));

        yield return ("HashRoute", Recording(Action("click", Element("link", "Order", Role("link", "Order")),
            effects: UrlChanged("https://app.test/#/orders/5"))));

        var radio = Element("radio", "Yes", Role("radio", "Yes", count: 2), Css("nameValue", "[name='insured'][value='yes']", "insured"));
        radio.Name = "insured";
        radio.Value = "yes";
        yield return ("RadioByValue", Recording(Action("check", radio, widget: "radio")));
    }

    private static string GoldenFolder([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "Golden");
}
