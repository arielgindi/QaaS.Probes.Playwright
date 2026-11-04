using QaaS.Playwright.Recorder;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Login;
using QaaS.Playwright.Recorder.Setup;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>
/// The rules a recording's setup is checked by, as a table: what the start screen refuses, and why, and the request it
/// builds from what it was given, for a new flow, one gone on from, and a recording never saved; with the login found,
/// none, or one a flow was recorded after.
/// </summary>
[TestFixture]
public class SetupRulesTests
{
    private static readonly string Project = Path.Combine(Path.GetTempPath(), "shop-tests");
    private static readonly string Flows = Path.Combine(Project, "Flows");
    private static readonly LoginFlow Login = new("LoginFlow", Path.Combine(Project, "bin", "Shop.Tests.dll"), null, Found: true);

    private static readonly SetupDraft NewOrder = new() { Project = Project, Name = "new-order", Url = "https://shop.test/orders" };

    private static SetupFacts Facts(params ProjectFlow[] flows) => new() { ProjectExists = true, Flows = flows };

    private static ProjectFlow Saved(string flow, string? after = null, bool kept = true) =>
        new(flow, Path.Combine(Flows, $"{flow}.cs"), $"hash-of-{flow}",
            kept ? new KeptRecording(new Recording("https://shop.test/helpdesk", []) { After = after }, DateTime.Now, $"{flow}.json") : null);

    private static IEnumerable<TestCaseData> Refused() =>
    [
        Case("no project", NewOrder with { Project = null }, Facts(), "project", "Choose the folder"),
        Case("a project that is gone", NewOrder, new SetupFacts(), "project", "is not there any more"),
        Case("no name", NewOrder with { Name = " " }, Facts(), "name", "Give the flow a name"),
        Case("a name of no letter", NewOrder with { Name = "-_-" }, Facts(), "name", "no letter or digit"),
        Case("a type the code uses", NewOrder with { Name = "task" }, Facts(), "name", "Try TaskFlow"),
        Case("a device on Windows", NewOrder with { Name = "con" }, Facts(), "name", "a device on Windows"),
        Case("no address", NewOrder with { Url = "" }, Facts(), "url", "Type the address"),
        Case("an address of no web page", NewOrder with { Url = "ftp://shop.test" }, Facts(), "url", "http:// or https://"),
        Case("a flow there already", NewOrder, Facts(Saved("NewOrder")), "ifExists", "NewOrder.cs is there already"),
        Case("continuing one never kept", NewOrder with { IfExists = IfExists.Continue }, Facts(Saved("NewOrder", kept: false)), "ifExists", "cannot be continued"),
        Case("nothing chosen to continue", NewOrder with { Tab = SetupTab.Continue }, Facts(Saved("NewTicket")), "continue", "Choose the flow"),
        Case("no flow to continue", NewOrder with { Tab = SetupTab.Continue }, Facts(), "continue", "record one under New flow"),
        Case("nothing to recover", NewOrder with { Tab = SetupTab.Recovered }, Facts(), "recovered", "no recording to recover"),
        Case("a login still looked for", NewOrder, Facts() with { LookingForLogin = true }, "login", "Start waits for it"),
        Case("a flow recorded after a login, continued without one", NewOrder with { Tab = SetupTab.Continue, ContinueFlow = "NewTicket", LogIn = false },
            Facts(Saved("NewTicket", after: "LoginFlow")), "login", "turn Log in first on"),
        Case("a flow recorded after a login the tests do not have now", NewOrder with { Tab = SetupTab.Continue, ContinueFlow = "NewTicket" },
            Facts(Saved("NewTicket", after: "TeamSignInFlow")) with { Login = Login }, "login", "do not have now"),
    ];

    [TestCaseSource(nameof(Refused))]
    public void ADraftThatCannotBeRecorded_IsRefused_ByTheFieldThatIsWrong(object draft, object facts, string field, string why)
    {
        var check = SetupRules.Check((SetupDraft)draft, (SetupFacts)facts);
        Assert.Multiple(() =>
        {
            Assert.That(check.Request, Is.Null);
            Assert.That(check.Problems, Does.ContainKey(field));
            Assert.That(check.Problems.GetValueOrDefault(field), Does.Contain(why));
        });
    }

    [Test]
    public void ANewFlow_IsTheRequestToRecordIt_WithTheLoginFound_IntoTheProjectsFolder()
    {
        var check = SetupRules.Check(NewOrder with { OutputDir = "UiFlows", KeepRecording = true, AutoChecks = false }, Facts() with { Login = Login });
        Assert.Multiple(() =>
        {
            Assert.That(check.Problems, Is.Empty);
            Assert.That(check.Request, Is.EqualTo(new RecordRequest("NewOrder", "https://shop.test/orders", Path.Combine(Project, "UiFlows"))
            {
                Login = Login, KeepRecording = true, AutoChecks = false,
            }));
            Assert.That(check.StartLabel, Is.EqualTo("Log in and start"));
            Assert.That(check.WillDo, Does.Contain("Chrome opens https://shop.test/orders, logs in with LoginFlow, then records NewOrder"));
        });
    }

    [Test]
    public void LoggingInOff_RecordsWithoutTheLoginFound()
    {
        var check = SetupRules.Check(NewOrder with { LogIn = false }, Facts() with { Login = Login });
        Assert.Multiple(() =>
        {
            Assert.That(check.Request?.Login, Is.Null);
            Assert.That(check.Request?.NoLogin, Is.True);
            Assert.That(check.StartLabel, Is.EqualTo("Start recording"));
        });
    }

    [Test]
    public void AFlowThereAlready_IsContinued_OrReplaced_AsChosen()
    {
        var facts = Facts(Saved("NewOrder"));
        var continued = SetupRules.Check(NewOrder with { IfExists = IfExists.Continue }, facts).Request;
        var replaced = SetupRules.Check(NewOrder with { IfExists = IfExists.Replace }, facts).Request;
        Assert.Multiple(() =>
        {
            Assert.That(continued, Is.Not.Null);
            Assert.That(continued!.Continue, Is.True);
            Assert.That(continued.Url, Is.EqualTo("https://shop.test/helpdesk"), "where its recording started");
            Assert.That(replaced, Is.Not.Null);
            Assert.That(replaced!.Continue, Is.False);
            Assert.That(replaced.Replaces, Is.EqualTo("hash-of-NewOrder"), "the file as it is now, and only it");
        });
    }

    [Test]
    public void ASavedFlow_IsContinued_AfterTheLoginItWasRecordedAfter_AsFoundOrAsItWasRecorded()
    {
        var found = SetupRules.Check(NewOrder with { Tab = SetupTab.Continue, ContinueFlow = "newticket" },
            Facts(Saved("NewTicket", after: "LoginFlow")) with { Login = Login });
        var recorded = new LoginFlow("TeamSignInFlow", "/elsewhere/Team.dll", null);
        var flow = Saved("NewTicket", after: "TeamSignInFlow");
        flow = flow with { Kept = flow.Kept! with { Recording = flow.Kept.Recording with { Autosave = new AutosavedFlow("NewTicket", Flows, true, recorded, null) } } };
        var asRecorded = SetupRules.Check(NewOrder with { Tab = SetupTab.Continue, ContinueFlow = "NewTicket" }, Facts(flow) with { Login = Login });
        Assert.Multiple(() =>
        {
            Assert.That(found.Request?.Continue, Is.True);
            Assert.That(found.Request?.ClassName, Is.EqualTo("NewTicket"));
            Assert.That(found.Request?.Login, Is.EqualTo(Login));
            Assert.That(found.StartLabel, Is.EqualTo("Continue NewTicket"));
            Assert.That(asRecorded.Request?.Login, Is.EqualTo(recorded));
        });
    }

    [Test]
    public void ARecordingNeverSaved_IsTakenUp_InTheFolderItWasRecordedFor()
    {
        var folder = Path.Combine(Path.GetTempPath(), "crm-tests", "Flows");
        var kept = new KeptRecording(new Recording("https://crm.test/", [])
        {
            Autosave = new AutosavedFlow("AddContact", folder, AutoChecks: false, null, Replaces: "h1", KeepRecording: true),
        }, DateTime.Now, "/recordings/AddContact-1.unsaved.json");
        var check = SetupRules.Check(NewOrder with { Tab = SetupTab.Recovered, Recovered = kept.Path, LogIn = false }, Facts() with { NotSaved = [kept] });
        Assert.That(check.Request, Is.EqualTo(new RecordRequest("AddContact", "https://crm.test/", folder)
        {
            Continue = true, AutoChecks = false, Replaces = "h1", KeepRecording = true, NoLogin = true,
        }));
    }

    [Test]
    public void AnAddressWithoutItsScheme_IsOfferedWithOne()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SetupRules.WithScheme("shop.test/orders"), Is.EqualTo("https://shop.test/orders"));
            Assert.That(SetupRules.WithScheme("https://shop.test"), Is.Null);
            Assert.That(SetupRules.WithScheme(""), Is.Null);
            Assert.That(SetupRules.For("2fa-login", "http://shop.test").ClassName, Is.EqualTo("Flow2faLogin"));
        });
    }

    private static TestCaseData Case(string name, SetupDraft draft, SetupFacts facts, string field, string why) =>
        new TestCaseData(draft, facts, field, why).SetName(string.Concat(name.Split(' ').Select(word => char.ToUpperInvariant(word[0]) + word[1..])));
}
