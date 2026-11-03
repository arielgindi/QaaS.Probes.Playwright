using System.Text.Json;
using QaaS.Playwright.Browser;

namespace QaaS.Playwright.Recorder.Login;

/// <summary>
/// The login flow last recorded with in each project folder, in <c>~/.qaas/recorder-logins.json</c>, so a plain
/// <c>record &lt;name&gt; &lt;url&gt;</c> there logs in the same way. <c>--no-login</c> forgets it.
/// </summary>
internal static class RememberedLogin
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static string FilePath => Path.Combine(Path.GetDirectoryName(BrowserDefaults.AuthStatePath)!, "recorder-logins.json");

    /// <summary>The login last used in <paramref name="projectFolder"/>; null when none, or the file cannot be read.</summary>
    public static LoginFlow? In(string projectFolder) => Read().GetValueOrDefault(Key(projectFolder));

    /// <summary>
    /// Remembers <paramref name="login"/> for <paramref name="projectFolder"/>, or forgets it when null. Returns why it
    /// could not, or null.
    /// </summary>
    public static string? Remember(string projectFolder, LoginFlow? login)
    {
        var logins = Read();
        var changed = login is null ? logins.Remove(Key(projectFolder)) : logins.GetValueOrDefault(Key(projectFolder)) != login;
        if (!changed) return null;
        if (login is not null) logins[Key(projectFolder)] = login;
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

    private static Dictionary<string, LoginFlow> Read()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Dictionary<string, LoginFlow>>(File.ReadAllText(FilePath), Json) ?? []
                : [];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            return []; // Only a convenience: the person can give the options again.
        }
    }

    private static string Key(string projectFolder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectFolder));
}
