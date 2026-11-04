using System.Text.Json;
using System.Text.Json.Serialization;
using QaaS.Playwright.Browser;
using QaaS.Playwright.Files;

namespace QaaS.Playwright.Recorder.Setup;

/// <summary>
/// What the person chose on the start screen for one project, to start from next time: where its flows go, the pages
/// recorded on last, newest first, the login's values file and the values typed that are not secret, the two switches,
/// and the tab used last. The login flow itself, or a no to it, is <see cref="Login.RememberedLogin"/>'s.
/// </summary>
internal sealed record ProjectChoices
{
    public required string Folder { get; init; }
    public DateTime LastUsed { get; init; }
    public string OutputDir { get; init; } = SetupRules.DefaultOutputDir;
    public IReadOnlyList<string> Urls { get; init; } = [];

    /// <summary>The YAML the login's values come from, when the person chose one other than the one found.</summary>
    public string? LoginSettings { get; init; }

    /// <summary>The values typed for the login flow, by its name then by the setting's; never a secret's.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Values { get; init; } = new Dictionary<string, IReadOnlyDictionary<string, string>>();

    public bool AutoChecks { get; init; } = true;
    public bool KeepRecording { get; init; }
    public SetupTab Tab { get; init; } = SetupTab.New;
}

/// <summary>
/// The start screen's memory, in <c>~/.qaas/recorder-projects.json</c>: the projects recorded in, newest first, and
/// what was chosen in each. It is only a convenience: a file that cannot be read is an empty memory, and one that
/// cannot be written says why, once, and the recording goes on.
/// </summary>
internal static class ProjectSettings
{
    /// <summary>How many projects the start screen offers as recent.</summary>
    public const int RecentCount = 8;

    private const int UrlsKept = 5;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true, Converters = { new JsonStringEnumConverter() },
    };

    private static string? _filePath;

    /// <summary>Where it is kept: <c>~/.qaas/recorder-projects.json</c>, or another file for the recorder's tests.</summary>
    internal static string FilePath
    {
        get => _filePath ??= Path.Combine(Path.GetDirectoryName(BrowserDefaults.AuthStatePath)!, "recorder-projects.json");
        set => _filePath = value;
    }

    /// <summary>The projects recorded in, the one used last first, at most <see cref="RecentCount"/>.</summary>
    public static IReadOnlyList<ProjectChoices> Recent() => [.. Read().OrderByDescending(project => project.LastUsed).Take(RecentCount)];

    /// <summary>What was chosen in <paramref name="folder"/>; null when nothing was yet.</summary>
    public static ProjectChoices? Of(string folder) => Read().FirstOrDefault(project => Same(project.Folder, folder));

    /// <summary>
    /// Keeps what was chosen in a project as it starts a recording: the start page goes first among the pages kept, and
    /// the project first among the recent ones. Returns why it could not, or null.
    /// </summary>
    public static string? Remember(ProjectChoices choices, string? url = null)
    {
        var projects = Read();
        var was = projects.FirstOrDefault(project => Same(project.Folder, choices.Folder));
        var urls = url is null ? choices.Urls : [url, .. choices.Urls.Where(kept => kept != url)];
        var now = choices with { Folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(choices.Folder)), LastUsed = DateTime.Now, Urls = [.. urls.Take(UrlsKept)] };
        projects.Remove(was!);
        projects.Add(now);
        return Write(projects);
    }

    /// <summary>Forgets <paramref name="folder"/>, as Remove in the recent projects asks. Returns why it could not, or null.</summary>
    public static string? Forget(string folder)
    {
        var projects = Read();
        return projects.RemoveAll(project => Same(project.Folder, folder)) == 0 ? null : Write(projects);
    }

    private static List<ProjectChoices> Read()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            return JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath), Json)?.Projects?.Where(project => project.Folder is { Length: > 0 }).ToList() ?? [];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return [];
        }
    }

    private static string? Write(List<ProjectChoices> projects)
    {
        try
        {
            FileStore.Machine.Write(FilePath, JsonSerializer.Serialize(new Stored { Projects = projects }, Json), ownerOnly: true);
            return null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return $"Your choices here are not remembered ({failure.Message}): check that you can write to {Path.GetDirectoryName(FilePath)}.";
        }
    }

    private static bool Same(string one, string other) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(one)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(other)),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed record Stored
    {
        public List<ProjectChoices>? Projects { get; init; }
    }
}
