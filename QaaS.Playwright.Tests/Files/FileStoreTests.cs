using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using QaaS.Playwright.Files;

namespace QaaS.Playwright.Tests.Files;

/// <summary>
/// The file store, through a file system that behaves as Windows does when it must: another program holding a file,
/// paths of either case one file, a crash halfway. And the source, so that nothing writes a file but the store.
/// </summary>
[TestFixture]
public class FileStoreTests
{
    private const string Flow = "/work/Tests/Flows/NewTicket.cs";
    private const string Yaml = "/work/Tests/Flows/NewTicket.flow.yaml";

    [Test]
    public void AFlowAndItsYaml_AreReplacedTogether_OrNotAtAll_EvenAfterACrashBetweenThem()
    {
        var (files, store) = Store();
        store.ReplaceTogether([(Flow, "code 1"), (Yaml, "yaml 1")]);
        // An editor holds the YAML for a moment, as Windows does a file another program reads.
        files.Hold(Yaml, times: 3);
        store.ReplaceTogether([(Flow, "code 2"), (Yaml, "yaml 2")]);
        var twice = (files.Read(Flow), files.Read(Yaml));

        // It holds it for good: the save fails, and the flow keeps its own YAML.
        files.Hold(Yaml, times: 1000);
        var held = Assert.Throws<IOException>(() => store.ReplaceTogether([(Flow, "code 3"), (Yaml, "yaml 3")]));
        files.Hold(Yaml, times: 0);
        var afterHeld = (files.Read(Flow), files.Read(Yaml), Leftovers(files));

        // The recorder dies between the two replacements; the next start puts the pair back as it was.
        files.CrashAt(Yaml);
        Assert.Throws<SimulatedFiles.Crash>(() => store.ReplaceTogether([(Flow, "code 4"), (Yaml, "yaml 4")]));
        files.CrashAt(null);
        var halfway = files.Read(Flow);
        var recovered = store.Recover("/work/Tests/Flows");

        Assert.Multiple(() =>
        {
            Assert.That(twice, Is.EqualTo(("code 2", "yaml 2")), "a held file is tried again");
            Assert.That(FileStore.Held(held!), Is.True);
            Assert.That(afterHeld, Is.EqualTo(("code 2", "yaml 2", "")), "neither replaced, nothing left beside them");
            Assert.That(halfway, Is.EqualTo("code 4"), "the crash came between the two");
            Assert.That((recovered, files.Read(Flow), files.Read(Yaml), Leftovers(files)), Is.EqualTo((1, "code 2", "yaml 2", "")));
            Assert.That(store.CreateTogether([("/work/Tests/Flows/NewTicket.recorded.txt", "c"), (Yaml, "y")]), Is.False,
                "a pair is created where neither is");
            Assert.That(files.Exists("/work/Tests/Flows/NewTicket.recorded.txt"), Is.False, "nothing of it written");
        });
    }

    [Test]
    public void AFolderIsOneKeyInEitherCase_WhereTheFileSystemSaysSo_ASecretIsNeverWritten_AndOwnedFoldersGoEvenWhenHeld()
    {
        var (windows, store) = Store(ignoresCase: true);
        var linux = new FileStore(new SimulatedFiles(ignoresCase: false));
        store.Write("/work/kept.json", "old");
        windows.Hold("/work/kept.json", times: 1000);
        Assert.Throws<IOException>(() => store.Write("/work/kept.json", "new"));
        windows.Hold("/work/kept.json", times: 0);
        var refused = Assert.Throws<SecretInFileException>(() =>
            store.WriteJson("/work/secret.json", new { User = "ada", Password = new Hidden() }, JsonSerializerOptions.Web));

        var owned = store.OwnedFolder("test");
        store.Write(Path.Combine(owned, "Flow.flow.yaml"), "Password: hunter2");
        windows.Hold(Path.Combine(owned, "Flow.flow.yaml"), times: 1000);
        var deletedWhileHeld = store.DeleteOwned(owned);
        windows.Hold(Path.Combine(owned, "Flow.flow.yaml"), times: 0);
        // The next start, once the run that owned it is gone.
        windows.Write(Path.Combine(owned, ".qaas-owned"), $"{ExitedProcess()}", createNew: false, ownerOnly: true);
        var swept = store.SweepOwned();

        Assert.Multiple(() =>
        {
            Assert.That(store.Key("/work/Tests/Flows/"), Is.EqualTo(store.Key("/WORK/tests/flows")));
            Assert.That(linux.Key("/work/Tests/Flows/"), Is.Not.EqualTo(linux.Key("/WORK/tests/flows")));
            Assert.That((windows.Read("/work/kept.json"), Leftovers(windows)), Is.EqualTo(("old", "")), "whole or not at all");
            Assert.That(refused!.Message, Does.Contain("secret.json"));
            Assert.That(windows.Exists("/work/secret.json"), Is.False);
            Assert.That(deletedWhileHeld, Is.False);
            Assert.That((swept, windows.Exists(Path.Combine(owned, "Flow.flow.yaml"))), Is.EqualTo((1, false)));
        });
    }

    /// <summary>The files that still write by themselves, until they write through the store; none may be added.</summary>
    private static readonly Dictionary<string, int> NotYetThroughTheStore = new()
    {
        ["QaaS.Playwright.Recorder/Autosave.cs"] = 3, ["QaaS.Playwright.Recorder/Capture/RecordingSession.cs"] = 1,
        ["QaaS.Playwright.Recorder/CrashReport.cs"] = 1, ["QaaS.Playwright.Recorder/FlowRecorder.cs"] = 8,
        ["QaaS.Playwright.Recorder/Login/ProjectBuilds.cs"] = 1, ["QaaS.Playwright.Recorder/Login/RememberedLogin.cs"] = 1,
        ["QaaS.Playwright.Recorder/Panel/PanelPrefs.cs"] = 1, ["QaaS.Playwright.Recorder/TestRun/FlowBuild.cs"] = 7,
        ["QaaS.Playwright/Browser/AtomicFileWriter.cs"] = 3, ["QaaS.Playwright/Browser/LocalChromeLauncher.cs"] = 1,
        ["QaaS.Playwright/Browser/StorageState.cs"] = 1, ["QaaS.Playwright/Files/FileStore.cs"] = 5,
    };

    [Test]
    public void NothingWritesAFile_ButTheStore()
    {
        var root = Path.GetFullPath(Path.Combine(Here(), "..", ".."));
        var writes = new Regex(@"File\.(WriteAll[A-Za-z]*|AppendAll[A-Za-z]*|Delete|Move|Replace|Copy|Create|OpenWrite)\(|new FileStream\(|" +
                               @"Directory\.Delete\(|new StreamWriter\(|AtomicFileWriter\.WriteAsync\(");
        var found = new[] { "QaaS.Playwright", "QaaS.Playwright.Recorder" }
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(root, project), "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(file => (File: Path.GetRelativePath(root, file).Replace('\\', '/'), Count: writes.Matches(File.ReadAllText(file)).Count))
            .Where(file => file.Count > 0).ToDictionary();
        var added = found.Where(file => file.Value > NotYetThroughTheStore.GetValueOrDefault(file.Key)).Select(file => $"{file.Key}: {file.Value}");
        Assert.That(added, Is.Empty, "write through FileStore, which waits for a held file, keeps a pair whole, and refuses a secret");
    }

    private static (SimulatedFiles, FileStore) Store(bool ignoresCase = false)
    {
        var files = new SimulatedFiles(ignoresCase);
        return (files, new FileStore(files, attempts: 5));
    }

    private static string Leftovers(SimulatedFiles files) => string.Join(", ", files.All.Where(path => path.Contains(".qaas-", StringComparison.Ordinal)));

    private static int ExitedProcess()
    {
        using var process = Process.Start(new ProcessStartInfo("dotnet", "--version") { RedirectStandardOutput = true })!;
        process.WaitForExit();
        return process.Id;
    }

    private static string Here([CallerFilePath] string here = "") => Path.GetDirectoryName(here)!;

    private sealed class Hidden : ISecretValue;
}

/// <summary>Files in memory, held by another program or lost to a crash where a test says, and of either case where it says.</summary>
internal sealed class SimulatedFiles(bool ignoresCase) : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(ignoresCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly HashSet<string> _folders = new(ignoresCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly Dictionary<string, int> _held = new(ignoresCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private string? _crashAt;
    private bool _dead;

    /// <summary>The process died: nothing more it does reaches the files.</summary>
    public sealed class Crash() : Exception("The recorder died here.");

    public bool IgnoresCase => ignoresCase;
    public IEnumerable<string> All => _files.Keys;

    /// <summary>The next <paramref name="times"/> writes, moves or deletes of the file fail as Windows' sharing violation does.</summary>
    public void Hold(string path, int times) => _held[path] = times;

    /// <summary>The process dies as it moves a file into <paramref name="path"/>; null, it lives again.</summary>
    public void CrashAt(string? path) => (_crashAt, _dead) = (path, false);

    public bool Exists(string path) => _files.ContainsKey(path);
    public string Read(string path) => _files.TryGetValue(path, out var text) ? text : throw new FileNotFoundException(path);

    public void Write(string path, string text, bool createNew, bool ownerOnly)
    {
        Touch(path);
        if (createNew && _files.ContainsKey(path)) throw new IOException($"{path} exists.");
        _files[path] = text;
    }

    public void Append(string path, string text)
    {
        Touch(path);
        _files[path] = _files.GetValueOrDefault(path, "") + text;
    }

    public void Move(string from, string to, bool overwrite)
    {
        Touch(from);
        Touch(to);
        if (_crashAt is not null && string.Equals(to, _crashAt, ignoresCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            _dead = true;
            throw new Crash();
        }

        if (!_files.Remove(from, out var text)) throw new FileNotFoundException(from);
        if (!overwrite && _files.ContainsKey(to)) throw new IOException($"{to} exists.");
        _files[to] = text;
    }

    public void Delete(string path)
    {
        Touch(path);
        _files.Remove(path);
    }

    public void CreateFolder(string path, bool ownerOnly) => _folders.Add(path);

    public void DeleteFolder(string path)
    {
        if (_files.Keys.Any(file => file.StartsWith(path + "/", StringComparison.Ordinal) && _held.GetValueOrDefault(file) > 0))
            throw Violation(path);
        foreach (var file in _files.Keys.Where(file => file.StartsWith(path + "/", StringComparison.Ordinal)).ToList()) _files.Remove(file);
        _folders.Remove(path);
    }

    public IEnumerable<string> Files(string folder, string pattern) =>
        _files.Keys.Where(file => Path.GetDirectoryName(file) == Path.TrimEndingDirectorySeparator(folder) && Matches(Path.GetFileName(file), pattern)).ToList();

    public IEnumerable<string> Folders(string folder, string pattern) =>
        _folders.Where(path => Path.GetDirectoryName(path) == Path.TrimEndingDirectorySeparator(folder) && Matches(Path.GetFileName(path), pattern)).ToList();

    private static bool Matches(string name, string pattern) =>
        Regex.IsMatch(name, "^" + Regex.Escape(pattern).Replace(@"\*", ".*") + "$");

    private void Touch(string path)
    {
        if (_dead) throw new Crash();
        if (_held.GetValueOrDefault(path) is > 0 and var left)
        {
            _held[path] = left - 1;
            throw Violation(path);
        }
    }

    // As Windows says it: ERROR_SHARING_VIOLATION.
    private static IOException Violation(string path) => new($"The process cannot access the file '{path}' because it is being used by another process.", unchecked((int)0x80070020));
}
