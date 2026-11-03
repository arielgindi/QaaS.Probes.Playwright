using QaaS.Playwright.Recorder;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>
/// The recorder's own copy of each recording, which no crash loses and a later recording continues.
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
}
