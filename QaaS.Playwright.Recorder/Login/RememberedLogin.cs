using System.Text.Json;
using QaaS.Playwright.Browser;

namespace QaaS.Playwright.Recorder.Login;

/// <summary>
/// The login flow last recorded with in each project folder, in <c>~/.qaas/recorder-logins.json</c>, so a plain
/// <c>record &lt;name&gt; &lt;url&gt;</c> there logs in the same way: the one given in the command, the one found in the
/// tests and taken, or none, when the person said no to the one found. <c>--no-login</c> forgets it.
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
    public static LoginFlow? In(string projectFolder) => Read().GetValueOrDefault(Key(projectFolder));

    /// <summary>The person said no to the login flow found in <paramref name="projectFolder"/>'s tests.</summary>
    public static bool Declined(string projectFolder) =>
        Read().TryGetValue(Key(projectFolder), out var login) && login is null;

    /// <summary>Remembers that the person said no to the login flow found there. Returns why it could not, or null.</summary>
    public static string? Decline(string projectFolder) => Write(projectFolder, null, declined: true);

    /// <summary>
    /// Remembers <paramref name="login"/> for <paramref name="projectFolder"/>, or forgets it when null. Returns why it
    /// could not, or null.
    /// </summary>
    public static string? Remember(string projectFolder, LoginFlow? login) => Write(projectFolder, login, declined: false);

    // A folder said no to has no login.
    private static string? Write(string projectFolder, LoginFlow? login, bool declined)
    {
        var logins = Read();
        var key = Key(projectFolder);
        var changed = login is null && !declined
            ? logins.Remove(key)
            : !logins.TryGetValue(key, out var was) || was != login;
        if (!changed) return null;
        if (login is not null || declined) logins[key] = login;
        try
        {
            AtomicFileWriter.WriteAsync(FilePath, JsonSerializer.Serialize(logins, Json)).GetAwaiter().GetResult();
            return null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return $"The login flow was not remembered in {FilePath}: {failure.Message}";
        }
    }

    private static Dictionary<string, LoginFlow?> Read()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Dictionary<string, LoginFlow?>>(File.ReadAllText(FilePath), Json) ?? []
                : [];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            return []; // Only a convenience: the person can give the options again.
        }
    }

    private static string Key(string projectFolder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectFolder));
}
