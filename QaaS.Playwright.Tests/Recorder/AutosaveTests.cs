using System.Runtime.CompilerServices;
using QaaS.Playwright.Recorder;
using QaaS.Playwright.Recorder.Capture;

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
            Captured.Action("fill", token, "tok-4711"));
        // Two projects, each with a Login flow.
        var shop = new RecordRequest("Login", recording.StartUrl, Path.Combine(_folder, "shop", "Flows"));
        var crm = shop with { OutputDir = Path.Combine(_folder, "crm", "Flows") };

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
            Assert.That(written, Has.Count.EqualTo(2), "the shop's saved, the CRM's not yet");
            foreach (var file in written)
            {
                Assert.That(File.ReadAllText(file), Does.Contain("ada").And.Not.Contain("hunter2").And.Not.Contain("tok-4711"), file);
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
        });
    }

    [Test]
    public void Continue_NamesTheFlowOrItsFile_AndSavingReplacesItOnlyWhileItIsAsRecorded()
    {
        var flows = Path.Combine(_folder, "Flows");
        var byFile = RecordRequest.Parse(["--continue", Path.Combine(flows, "ScheduleMaintenance.cs"), "--no-panel"]);
        var byName = RecordRequest.Parse(["--continue", "schedule-maintenance", "--output-dir", flows]);
        var recording = Recording.FromJson(File.ReadAllText(Path.Combine(GoldenFolder(), "ScheduleMaintenance.recording.json")));
        var whole = FlowCodeGenerator.Generate(recording, "ScheduleMaintenance", "Flows");
        var start = FlowCodeGenerator.Generate(recording with { Actions = [.. recording.Actions.Take(2)] }, "ScheduleMaintenance", "Flows");

        var created = FlowRecorder.Save(byName, start);
        var replaced = FlowRecorder.Save(byName with { Replaces = Autosave.Hash(start.Code) }, whole);
        File.AppendAllText(created.CodePath, "// Changed by hand.\n");
        var beside = FlowRecorder.Save(byName with { Replaces = Autosave.Hash(whole.Code) }, start);

        Assert.Multiple(() =>
        {
            Assert.That((byFile.ClassName, byFile.OutputDir, byFile.Continue, byFile.Panel), Is.EqualTo(("ScheduleMaintenance", flows, true, false)));
            Assert.That((byName.ClassName, byName.CodePath, byName.Continue), Is.EqualTo(("ScheduleMaintenance", created.CodePath, true)));
            Assert.That(replaced, Is.EqualTo(created), "the flow continued is replaced");
            Assert.That(beside.CodePath, Does.EndWith("ScheduleMaintenance.recorded.txt"), "a flow changed by hand never is");
            Assert.That(File.ReadAllText(created.CodePath), Is.EqualTo(whole.Code + "// Changed by hand.\n"));
        });
    }

    private static string GoldenFolder([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "Golden");
}
