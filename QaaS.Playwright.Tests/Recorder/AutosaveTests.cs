using System.Runtime.CompilerServices;
using QaaS.Playwright.Recorder;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Generation;
using QaaS.Playwright.Recorder.Setup;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>
/// The recorder's own copy of each recording, which no crash loses and a later recording continues, and saving a flow
/// continued, which never overwrites what the person changed.
/// </summary>
[TestFixture]
public class AutosaveTests
{
    private string _folder = null!;

    [SetUp]
    public void UseAFolderOfItsOwn()
    {
        _folder = Directory.CreateTempSubdirectory("qaas-autosave-").FullName;
        Autosave.Folder = Path.Combine(_folder, "recordings");
    }

    [TearDown]
    public void DeleteIt()
    {
        Autosave.Folder = null!;
        Directory.Delete(_folder, recursive: true);
    }

    [Test]
    public async Task Autosave_WritesNoSecret_ForItsOwnerOnly_OneFilePerFolderAndFlow_AndKeepsItOnceSaved()
    {
        var password = Captured.Element("textbox", "Password", Captured.Css("name", "[name='password']", "password"));
        password.Type = "password";
        var token = Captured.Element("textbox", "Token", Captured.Css("name", "[name='apiToken']", "apiToken"));
        var recording = Captured.Recording(
            Captured.Action("fill", Captured.Element("textbox", "User", Captured.Css("name", "[name='user']", "user")), "ada"),
            Captured.Action("fill", password, "hunter2"),
            Captured.Action("fill", token, "tok-4711"),
            Captured.Action("expect", Captured.Element("heading", "Welcome", Captured.Role("heading", "Welcome"))));
        // The person's edits and checks are kept with it, but not a default they gave a secret.
        recording.Actions[0].Edit = new ActionEdit { Step = "SignInAsync", Values = new() { ["value"] = new ValueEdit { Unique = true } } };
        recording.Actions[2].Edit = new ActionEdit { Values = new() { ["value"] = new ValueEdit { Value = "tok-9999" } } };
        recording.Actions[3].Check = "visible";
        recording = recording with { AutoChecks = false };
        // Two projects, each with a Login flow.
        var shop = new RecordRequest("Login", recording.StartUrl, Path.Combine(_folder, "shop", "Flows"));
        var crm = shop with { OutputDir = Path.Combine(_folder, "crm", "Flows"), KeepRecording = true };

        await new Autosave().WriteAsync(recording, shop);
        await new Autosave().WriteAsync(recording, crm);
        await File.WriteAllTextAsync(Path.Combine(Autosave.Folder, "Broken-0123456789ab.unsaved.json"), "{ not a recording");
        var notSaved = Autosave.NotSaved();

        var saved = new SavedFlow(Path.Combine(shop.OutputDir, "Login.cs"), Path.Combine(shop.OutputDir, "Login.flow.yaml"));
        await new Autosave().SavedAsync(recording, shop, saved, "// Login's code");
        var written = Directory.GetFiles(Autosave.Folder).Where(file => !file.Contains("Broken")).ToList();
        var kept = Autosave.Find(shop.OutputDir, "Login")!;
        Directory.CreateDirectory(shop.OutputDir);
        await File.WriteAllTextAsync(saved.YamlPath, "FlowConfiguration:\n  Login:\n    Password: 'hunter2'\n");
        var unknown = Autosave.Unkept(kept.Recording, "Login");
        var unknownWithItsValues = Autosave.Unkept(kept.Recording, "Login", saved.YamlPath);

        Assert.Multiple(() =>
        {
            Assert.That(notSaved.Select(found => found.Recording.Autosave!.Folder), Is.EquivalentTo(new[] { shop.OutputDir, crm.OutputDir }),
                "one file per folder and flow, and the broken one skipped");
            Assert.That(notSaved.Single(found => found.Recording.Autosave!.Folder == crm.OutputDir).Recording.Autosave!.KeepRecording, Is.True,
                "a recording recovered keeps its raw recording beside the flow, as --keep-recording asked");
            Assert.That(written, Has.Count.EqualTo(2), "the shop's saved, the CRM's not yet");
            foreach (var file in written)
            {
                Assert.That(File.ReadAllText(file), Does.Contain("ada").And.Not.Contain("hunter2").And.Not.Contain("tok-4711")
                    .And.Not.Contain("tok-9999"), file);
                if (!OperatingSystem.IsWindows())
                    Assert.That(File.GetUnixFileMode(file), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite), file);
            }

            if (!OperatingSystem.IsWindows())
                Assert.That(File.GetUnixFileMode(Autosave.Folder), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));

            Assert.That(Autosave.NotSaved().Select(found => found.Recording.Autosave!.Folder), Is.EqualTo(new[] { crm.OutputDir }),
                "a recording saved is offered no more");
            Assert.That(kept.Path, Does.Not.EndWith(".unsaved.json"));
            Assert.That(kept.Recording.Autosave!.Replaces, Is.EqualTo(Autosave.Hash("// Login's code")));
            Assert.That(unknown, Is.EqualTo(new[] { "Password", "ApiToken" }));
            Assert.That(unknownWithItsValues, Is.EqualTo(new[] { "ApiToken" }), "the flow's own values give the password");
            Assert.That(kept.Recording.Actions[1].Value, Is.EqualTo("hunter2"));
            Assert.That((kept.Recording.Actions[0].Edit?.Step, kept.Recording.Actions[0].Edit?.Values?["value"].Unique,
                    kept.Recording.Actions[3].Check, kept.Recording.AutoChecks),
                Is.EqualTo(("SignInAsync", (bool?)true, "visible", (bool?)false)), "the person's edits and checks are kept");
        });
    }

    [Test]
    public void Continue_NamesTheFlowOrItsFile_AndSavingReplacesItOnlyWhileItIsAsRecorded()
    {
        var flows = Path.Combine(_folder, "Flows");
        // As the start screen's Continue tab chooses it: the flow of the folder, with its kept recording.
        var kept = new KeptRecording(new Recording("https://fleet.test/vehicles", []), DateTime.Now, "ScheduleMaintenance.json");
        var byName = SetupRules.Check(new SetupDraft { Project = _folder, Tab = SetupTab.Continue, ContinueFlow = "schedulemaintenance", LogIn = false },
            new SetupFacts { ProjectExists = true, Flows = [new ProjectFlow("ScheduleMaintenance", Path.Combine(flows, "ScheduleMaintenance.cs"), "h", kept)] }).Request!;
        var recording = Recording.FromJson(File.ReadAllText(Path.Combine(GoldenFolder(), "ScheduleMaintenance.recording.json")));
        var whole = FlowCodeGenerator.Generate(recording, "ScheduleMaintenance", "Flows");
        var start = FlowCodeGenerator.Generate(recording with { Actions = [.. recording.Actions.Take(2)] }, "ScheduleMaintenance", "Flows");

        var created = FlowRecorder.Save(byName, start);
        var replaced = FlowRecorder.Save(byName with { Replaces = Autosave.Hash(start.Code) }, whole);
        File.AppendAllText(created.CodePath, "// Changed by hand.\n");
        var beside = FlowRecorder.Save(byName with { Replaces = Autosave.Hash(whole.Code) }, start);
        // Continued without the steps that did not replay, as when the app was signed in already: the flow keeps them.
        var posters = byName with { ClassName = "Posters" };
        var recorded = FlowRecorder.Save(posters, whole);
        var leftOut = FlowRecorder.Save(FlowRecorder.LeftOut(posters with { Replaces = Autosave.Hash(whole.Code) }), start);
        // A test project of flows, and a tool's project above a scratch folder.
        Directory.CreateDirectory(Path.Combine(_folder, "Shop.Tests", "Flows"));
        File.WriteAllText(Path.Combine(_folder, "Shop.Tests", "Shop.Tests.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"QaaS.Playwright\" /></ItemGroup></Project>");
        Directory.CreateDirectory(Path.Combine(_folder, "scratch", "overnight", "Flows"));
        File.WriteAllText(Path.Combine(_folder, "scratch", "recorder.final.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"..\\QaaS.Playwright\\QaaS.Playwright.csproj\" /></ItemGroup></Project>");
        var (tests, scratch) = (ProjectNamespace.Of(Path.Combine(_folder, "Shop.Tests", "Flows")), ProjectNamespace.Of(Path.Combine(_folder, "scratch", "overnight", "Flows")));

        Assert.Multiple(() =>
        {
            Assert.That((byName.ClassName, byName.OutputDir, byName.Url), Is.EqualTo(("ScheduleMaintenance", flows, "https://fleet.test/vehicles")));
            Assert.That((byName.ClassName, byName.CodePath, byName.Continue), Is.EqualTo(("ScheduleMaintenance", created.CodePath, true)));
            Assert.That(replaced, Is.EqualTo(created), "the flow continued is replaced");
            Assert.That(beside.CodePath, Does.EndWith("ScheduleMaintenance.recorded.txt"), "a flow changed by hand never is");
            Assert.That(File.ReadAllText(created.CodePath), Is.EqualTo(whole.Code + "// Changed by hand.\n"));
            Assert.That((Path.GetFileName(leftOut.CodePath), File.ReadAllText(recorded.CodePath)), Is.EqualTo(("Posters.recorded.txt", whole.Code)),
                "a flow continued without steps is saved beside the flow, which keeps them");
            Assert.That((tests, scratch), Is.EqualTo(("Shop.Tests.Flows", "Flows")),
                "the namespace of the project the flow builds in, never of a project of something else above a scratch folder");
        });
    }

    [TestCase("a change of address it does not give")]
    [TestCase("a field named with underscores alone")]
    [TestCase("a field in a widget of an id of underscores")]
    [TestCase("a button of an id of underscores")]
    public async Task ARecording_SavingFailedOn_IsKeptAndWritten(string what)
    {
        // Saving these failed with "ArgumentNullException: Value cannot be null", and so did keeping the field's.
        var action = what switch
        {
            "a change of address it does not give" => Captured.Action("click", Captured.Element("button", "Save", Captured.Role("button", "Save")),
                null, null, null, new CapturedEffect { Kind = "urlChanged", Page = "page" }),
            "a field named with underscores alone" => Captured.Action("fill", Captured.Element("textbox", "", Captured.Css("name", "[name='___']", "___")), "text"),
            "a field in a widget of an id of underscores" => Captured.Action("fill",
                Captured.Element("textbox", "", Captured.Css("within", "[id='___'] input", "___")), "place"),
            _ => Captured.Action("click", Captured.Element("button", "Save", Captured.Css("id", "[id='___']", "___"))),
        };
        var request = new RecordRequest("Unnamed", "https://app.test/start", Path.Combine(_folder, "Flows"));

        await new Autosave().WriteAsync(Captured.Recording(action), request);
        var kept = Autosave.NotSaved().Single().Recording;
        var code = FlowCodeGenerator.Generate(kept, "Unnamed", "Flows").Code;

        Assert.That(code, Does.Contain(action.Kind == "fill" ? "FillAsync(Configuration." : "ClickAsync();").And.Not.Contain("ToHaveURLAsync"));
    }

    [Test]
    public void AnActionThePlannerFailsOn_IsLeftOutAndSaid_TheRestIsWritten_AndKeepingNeedsNoPlan()
    {
        var token = Captured.Element("textbox", "Token", Captured.Css("name", "[name='apiToken']", "apiToken"));
        var recording = Captured.Recording(
            Captured.Action("click", Captured.Element("button", "Open", Captured.Role("button", "Open"))),
            Captured.Action("click", Captured.Element("button", "Broken", Captured.Role("button", "Broken"))),
            Captured.Action("fill", token),
            Captured.Action("click", Captured.Element("button", "Save", Captured.Role("button", "Save"))));
        var broken = recording.Actions[1].Id;
        // A planner bug one action sets off, wherever it is in the recording.
        FlowPlan Planner(ValidRecording valid, string name) =>
            valid.Actions.Any(action => action.Id == broken) ? throw new InvalidOperationException("a bug") : FlowPlanner.Plan(valid, name);

        var (plan, flow) = FlowCodeGenerator.PlanAndWrite(RecordingReader.Read(recording), "Isolated", "Flows", planner: Planner);
        var unkept = Autosave.Unkept(recording, "Isolated", planner: (_, _) => throw new InvalidOperationException("a bug"));

        Assert.Multiple(() =>
        {
            Assert.That(flow.Code, Does.Contain("\"Open\"").And.Contain("\"Save\"").And.Not.Contain("\"Broken\""));
            Assert.That(plan.NotRecorded, Has.One.StartWith($"{FlowCodeGenerator.NotPlanned} {broken} (a 'click'), as InvalidOperationException"));
            Assert.That(unkept, Is.EqualTo(new[] { "ApiToken" }), "the value kept without its secret, named by its field as no plan is made");
        });
    }

    private static string GoldenFolder([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "Golden");
}
