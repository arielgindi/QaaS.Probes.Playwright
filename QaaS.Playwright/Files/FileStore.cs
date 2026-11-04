using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QaaS.Playwright.Files;

/// <summary>What the file store needs of a file system: the machine's (<see cref="MachineFiles"/>), or a simulated one.</summary>
internal interface IFileSystem
{
    /// <summary>Whether two spellings of a path that differ in case are one file, as on Windows and macOS.</summary>
    bool IgnoresCase { get; }

    bool Exists(string path);
    string Read(string path);

    /// <summary>Writes the whole file; <paramref name="createNew"/>, only when there is none yet (else an IOException).</summary>
    void Write(string path, string text, bool createNew, bool ownerOnly);

    void Append(string path, string text);
    void Move(string from, string to, bool overwrite);
    void Delete(string path);
    void CreateFolder(string path, bool ownerOnly);
    void DeleteFolder(string path);
    IEnumerable<string> Files(string folder, string pattern);
    IEnumerable<string> Folders(string folder, string pattern);
}

/// <summary>A value that must never be written to a file: the recording's secrets are (their type implements it).</summary>
internal interface ISecretValue;

/// <summary>A file the store refused to write, as it would have held a secret.</summary>
internal sealed class SecretInFileException(string path) : InvalidOperationException($"{path} would hold a secret: it is not written.");

/// <summary>
/// Every file the recorder and the probe write goes through here. A file is written whole or not at all (a temporary
/// file beside it, renamed over it), for its owner only when it says so. Windows refuses to replace or delete a file
/// another program holds (an editor, a sync client, a scanner): the store waits and tries again before it gives up. A
/// flow and its YAML are replaced together or not at all, the originals kept until both are in place, and a pair left
/// half replaced by a crash is finished or undone the next time (<see cref="Recover"/>). A folder's identity
/// (<see cref="Key"/>) follows the file system's rule for case. A temporary folder it owns (a test build's, with
/// secrets in it) is deleted, and deleted again at the next start when a program still held it.
/// </summary>
internal sealed class FileStore(IFileSystem files, int attempts = 8)
{
    private const string Staged = ".qaas-new";
    private const string Backup = ".qaas-old";
    private const string Journal = ".qaas-journal";
    private const string OwnedMark = ".qaas-owned";

    /// <summary>The machine's files.</summary>
    public static FileStore Machine { get; } = new(new MachineFiles());

    public IFileSystem Files => files;

    /// <summary>The one identity of <paramref name="path"/>: its full path, in one case where the file system ignores case.</summary>
    public string Key(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return files.IgnoresCase ? full.ToUpperInvariant() : full;
    }

    /// <summary>Writes <paramref name="text"/> to <paramref name="path"/>, whole or not at all.</summary>
    public void Write(string path, string text, bool ownerOnly = false)
    {
        var target = Path.GetFullPath(path);
        files.CreateFolder(Path.GetDirectoryName(target)!, ownerOnly: false);
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, $".qaas-{Guid.NewGuid():N}.tmp");
        try
        {
            files.Write(temporary, text, createNew: true, ownerOnly);
            Retried(() => files.Move(temporary, target, overwrite: true));
        }
        finally
        {
            if (files.Exists(temporary)) Retried(() => files.Delete(temporary));
        }
    }

    /// <summary>Writes <paramref name="value"/> as JSON; a secret in it, anywhere, is refused, and nothing is written.</summary>
    public void WriteJson<T>(string path, T value, JsonSerializerOptions options, bool ownerOnly = false)
    {
        var refusing = new JsonSerializerOptions(options);
        refusing.Converters.Insert(0, new RefuseSecrets(path));
        Write(path, JsonSerializer.Serialize(value, refusing), ownerOnly);
    }

    /// <summary>Adds <paramref name="text"/> at the end of <paramref name="path"/>, as a log does.</summary>
    public void Append(string path, string text)
    {
        files.CreateFolder(Path.GetDirectoryName(Path.GetFullPath(path))!, ownerOnly: false);
        Retried(() => files.Append(path, text));
    }

    public void Delete(string path)
    {
        if (files.Exists(path)) Retried(() => files.Delete(path));
    }

    /// <summary>
    /// Writes each file only where none is yet, all of them or none: false, and nothing written, when one is there.
    /// </summary>
    public bool CreateTogether(IReadOnlyList<(string Path, string Text)> written)
    {
        var made = new List<string>();
        try
        {
            foreach (var (path, text) in written)
            {
                files.CreateFolder(Path.GetDirectoryName(Path.GetFullPath(path))!, ownerOnly: false);
                if (files.Exists(path)) return Undo(made);
                files.Write(path, text, createNew: true, ownerOnly: false);
                made.Add(path);
            }

            return true;
        }
        catch (IOException) when (written.Any(file => !made.Contains(file.Path) && files.Exists(file.Path)))
        {
            // One appeared meanwhile: it is not overwritten.
            return Undo(made);
        }
        catch
        {
            Undo(made);
            throw;
        }
    }

    /// <summary>
    /// Replaces each file with its text, all of them or none: each is staged beside its target first, and the originals
    /// are kept until every one is in place; a failure puts them back. A journal beside the first lets
    /// <see cref="Recover"/> finish or undo a replacement a crash interrupted.
    /// </summary>
    public void ReplaceTogether(IReadOnlyList<(string Path, string Text)> replaced)
    {
        var targets = replaced.Select(file => Path.GetFullPath(file.Path)).ToList();
        var journal = targets[0] + Journal;
        foreach (var (target, (_, text)) in targets.Zip(replaced))
        {
            files.CreateFolder(Path.GetDirectoryName(target)!, ownerOnly: false);
            Retried(() => files.Write(target + Staged, text, createNew: false, ownerOnly: false));
        }

        files.Write(journal, JsonSerializer.Serialize(targets), createNew: false, ownerOnly: false);
        try
        {
            foreach (var target in targets)
            {
                if (files.Exists(target)) Retried(() => files.Move(target, target + Backup, overwrite: true));
                Retried(() => files.Move(target + Staged, target, overwrite: false));
            }
        }
        catch
        {
            RollBack(targets);
            throw;
        }

        foreach (var target in targets) Delete(target + Backup);
        Delete(journal);
    }

    /// <summary>
    /// Finishes, or undoes, each replacement in <paramref name="folder"/> that a crash interrupted: undone while one of
    /// its files was still staged, else finished. Returns how many it found.
    /// </summary>
    public int Recover(string folder)
    {
        var found = 0;
        foreach (var journal in files.Files(folder, "*" + Journal).ToList())
        {
            found++;
            var targets = JsonSerializer.Deserialize<List<string>>(files.Read(journal)) ?? [];
            if (targets.Any(target => files.Exists(target + Staged))) RollBack(targets);
            else foreach (var target in targets) Delete(target + Backup);
            Delete(journal);
        }

        return found;
    }

    /// <summary>A new temporary folder of the store's, for files that must not outlive this run, e.g. with secrets.</summary>
    public string OwnedFolder(string name)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"qaas-{name}-{Guid.NewGuid():N}");
        files.CreateFolder(folder, ownerOnly: true);
        files.Write(Path.Combine(folder, OwnedMark), $"{Environment.ProcessId}", createNew: true, ownerOnly: true);
        return folder;
    }

    /// <summary>Deletes an owned folder, each of its files first; false when a program still holds one, for the next start.</summary>
    public bool DeleteOwned(string folder)
    {
        try
        {
            foreach (var file in files.Files(folder, "*").Where(file => Path.GetFileName(file) != OwnedMark).ToList()) Retried(() => files.Delete(file));
            Retried(() => files.DeleteFolder(folder));
            return true;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Deletes the owned folders that a run before this one could not, once no running process owns them.</summary>
    public int SweepOwned()
    {
        var swept = 0;
        foreach (var folder in files.Folders(Path.GetTempPath(), "qaas-*").ToList())
        {
            var mark = Path.Combine(folder, OwnedMark);
            if (!files.Exists(mark) || !int.TryParse(files.Read(mark), out var owner) || owner == Environment.ProcessId || Alive(owner)) continue;
            if (DeleteOwned(folder)) swept++;
        }

        return swept;
    }

    /// <summary>
    /// Whether <paramref name="failure"/> says another program holds the file for now (Windows' sharing and lock
    /// violations), which passes, rather than that it cannot be written.
    /// </summary>
    public static bool Held(Exception failure) => failure is IOException { HResult: var result } && (result & 0xFFFF) is 32 or 33;

    private void Retried(Action write)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                write();
                return;
            }
            catch (Exception failure) when (Held(failure) && attempt < attempts)
            {
                Thread.Sleep(Math.Min(25 << attempt, 800));
            }
        }
    }

    // Each target as it was: its original back, or none where there was none; what was staged, gone.
    private void RollBack(IEnumerable<string> targets)
    {
        foreach (var target in targets)
        {
            if (files.Exists(target + Backup)) Retried(() => files.Move(target + Backup, target, overwrite: true));
            else if (!files.Exists(target + Staged) && files.Exists(target)) Retried(() => files.Delete(target));
            Delete(target + Staged);
        }

        Delete(targets.First() + Journal);
    }

    private bool Undo(List<string> made)
    {
        foreach (var path in made) Delete(path);
        return false;
    }

    private static bool Alive(int process)
    {
        try
        {
            using var running = Process.GetProcessById(process);
            return !running.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private sealed class RefuseSecrets(string path) : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeof(ISecretValue).IsAssignableFrom(typeToConvert);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(Refuse<>).MakeGenericType(typeToConvert), path)!;

        private sealed class Refuse<T>(string path) : JsonConverter<T>
        {
            public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                throw new SecretInFileException(path);

            public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
                throw new SecretInFileException(path);
        }
    }
}

/// <summary>The machine's files.</summary>
internal sealed class MachineFiles : IFileSystem
{
    private const UnixFileMode OwnerFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public bool IgnoresCase { get; } = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public bool Exists(string path) => File.Exists(path);
    public string Read(string path) => File.ReadAllText(path);

    public void Write(string path, string text, bool createNew, bool ownerOnly)
    {
        var options = new FileStreamOptions { Mode = createNew ? FileMode.CreateNew : FileMode.Create, Access = FileAccess.Write };
        if (ownerOnly && !OperatingSystem.IsWindows()) options.UnixCreateMode = OwnerFile;
        using var file = new StreamWriter(path, options);
        file.Write(text);
    }

    public void Append(string path, string text) => File.AppendAllText(path, text);
    public void Move(string from, string to, bool overwrite) => File.Move(from, to, overwrite);
    public void Delete(string path) => File.Delete(path);

    public void CreateFolder(string path, bool ownerOnly)
    {
        if (ownerOnly && !OperatingSystem.IsWindows()) Directory.CreateDirectory(path, OwnerFile | UnixFileMode.UserExecute);
        else Directory.CreateDirectory(path);
    }

    public void DeleteFolder(string path) => Directory.Delete(path, recursive: true);
    public IEnumerable<string> Files(string folder, string pattern) => Directory.Exists(folder) ? Directory.EnumerateFiles(folder, pattern) : [];
    public IEnumerable<string> Folders(string folder, string pattern) => Directory.Exists(folder) ? Directory.EnumerateDirectories(folder, pattern) : [];
}
