using System.Text.Json;
using System.Text.Json.Nodes;
using QaaS.Playwright.Browser;

namespace QaaS.Playwright.Recorder.Login;

/// <summary>A "no" to logging in, remembered for a folder: when it was said, and to which flow, when one was found.</summary>
internal sealed record DeclinedLogin(DateTime? On, string? Flow);

/// <summary>
/// The login flow last recorded with in each project folder, in <c>~/.qaas/recorder-logins.json</c>, so a plain
/// <c>record &lt;name&gt; &lt;url&gt;</c> there logs in the same way: the one given in the command, the one found in the
/// tests and taken, or none, when the person said no to the one found, with the day they said it. The test project the
/// person chose there, when several were found, is kept with it. <c>--no-login</c> forgets it.
/// </summary>
internal static class RememberedLogin
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static string? _filePath;

    /// <summary>Where the logins are kept: <c>~/.qaas/recorder-logins.json</c>, or another file for the recorder's tests.</summary>
    internal static string FilePath
    {
        get => _filePath ??= Path.Combine(Path.GetDirectoryName(BrowserDefaults.AuthStatePath)!, "recorder-logins.json");
        set => _filePath = value;
    }

    /// <summary>The login last used in <paramref name="projectFolder"/>; null when none, or the file cannot be read.</summary>
    public static LoginFlow? In(string projectFolder) => Read().GetValueOrDefault(Key(projectFolder))?.Login;

    /// <summary>The "no" said in <paramref name="projectFolder"/> to the login flow found there; null when none was.</summary>
    public static DeclinedLogin? DeclinedIn(string projectFolder) =>
        Read().TryGetValue(Key(projectFolder), out var entry) && entry.Declined ? new DeclinedLogin(entry.On, entry.Flow) : null;

    /// <summary>The test project the person chose in <paramref name="projectFolder"/>, its .csproj; null when none.</summary>
    public static string? ProjectIn(string projectFolder) => Read().GetValueOrDefault(Key(projectFolder))?.Project;

    /// <summary>Anything is remembered for <paramref name="projectFolder"/>: a login, a no, or a project chosen.</summary>
    public static bool HasAnswer(string projectFolder) => Read().ContainsKey(Key(projectFolder));

    /// <summary>
    /// Remembers that the person said no to <paramref name="flow"/>, the login flow found there, or to logging in.
    /// Returns why it could not, or null.
    /// </summary>
    public static string? Decline(string projectFolder, string? flow = null) =>
        Write(projectFolder, was => new Entry(null, was?.Project, Declined: true, DateTime.Now, flow));

    /// <summary>Remembers the test project the person chose there. Returns why it could not, or null.</summary>
    public static string? RememberProject(string projectFolder, string project) =>
        Write(projectFolder, was => (was ?? new Entry(null, null, false, null, null)) with { Project = project });

    /// <summary>
    /// Remembers <paramref name="login"/> for <paramref name="projectFolder"/>, or forgets everything there when null.
    /// Returns why it could not, or null.
    /// </summary>
    public static string? Remember(string projectFolder, LoginFlow? login) =>
        Write(projectFolder, was => login is null ? null : new Entry(login, login.Project ?? was?.Project, false, null, null));

    // What a folder has: a login, or a no to one, and the test project chosen there.
    private sealed record Entry(LoginFlow? Login, string? Project, bool Declined, DateTime? On, string? Flow);

    private static string? Write(string projectFolder, Func<Entry?, Entry?> change)
    {
        var entries = Read();
        var key = Key(projectFolder);
        var was = entries.GetValueOrDefault(key);
        var now = change(was);
        if (now == was) return null;
        if (now is null) entries.Remove(key);
        else entries[key] = now;
        try
        {
            AtomicFileWriter.WriteAsync(FilePath, JsonSerializer.Serialize(entries, Json)).GetAwaiter().GetResult();
            return null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return $"The recorder could not remember your answer in {FilePath}: {failure.Message}. It asks again next " +
                   "time; check that you can write to that folder.";
        }
    }

    // The file of 2.0.2 and before kept a login, or null for a no, in each folder: both are read as they were.
    private static Dictionary<string, Entry> Read()
    {
        try
        {
            if (!File.Exists(FilePath) || JsonNode.Parse(File.ReadAllText(FilePath)) is not JsonObject folders) return [];
            var entries = new Dictionary<string, Entry>();
            foreach (var (folder, value) in folders)
                entries[folder] = value switch
                {
                    null => new Entry(null, null, Declined: true, null, null),
                    JsonObject entry when entry.ContainsKey("login") || entry.ContainsKey("declined") || entry.ContainsKey("project")
                        => entry.Deserialize<Entry>(Json) ?? new Entry(null, null, false, null, null),
                    _ => new Entry(value.Deserialize<LoginFlow>(Json), null, false, null, null),
                };
            return entries;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException
                                            or InvalidOperationException)
        {
            return []; // Only a convenience: the person can give the options again.
        }
    }

    private static string Key(string projectFolder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectFolder));
}
