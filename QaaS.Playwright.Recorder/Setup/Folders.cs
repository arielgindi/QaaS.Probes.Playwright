using QaaS.Playwright.Recorder.Login;

namespace QaaS.Playwright.Recorder.Setup;

/// <summary>
/// The folders the start screen deals in: the project the recorder was started in, when that folder means anything,
/// and the window's folder browser, which lists folders, and DLLs or YAML files when it picks one; never a file's
/// contents.
/// </summary>
internal static class Folders
{
    /// <summary>The process's folder; the home folder when it was deleted from under it.</summary>
    public static string Here
    {
        get
        {
            try
            {
                return Environment.CurrentDirectory;
            }
            catch (IOException)
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
        }
    }

    private static readonly HashSet<string> Skipped = new(["bin", "obj", "node_modules"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The project in <paramref name="folder"/>, the one the recorder was started in: the nearest test project at or
    /// above it, else the folder itself; null when the folder means nothing, as the tool's own, the system's, the home
    /// folder, the Desktop or a root are for a recorder started from a shortcut or a double-click.
    /// </summary>
    public static string? ProjectIn(string folder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (!Meaningful(full)) return null;
        return TestProject.Near(full) is { } project && Meaningful(project.Folder) ? project.Folder : full;
    }

    public static bool Meaningful(string folder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (!Directory.Exists(full) || Path.GetPathRoot(full) is { } root && Same(Path.TrimEndingDirectorySeparator(root), full)) return false;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] meaningless =
        [
            home, Environment.GetFolderPath(Environment.SpecialFolder.Desktop), Environment.SystemDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), Path.GetTempPath(),
        ];
        if (meaningless.Any(each => each.Length > 0 && Same(Path.TrimEndingDirectorySeparator(each), full))) return false;
        string[] under = [AppContext.BaseDirectory, Path.Combine(home, ".dotnet", "tools")];
        return !under.Any(each => full.StartsWith(Path.TrimEndingDirectorySeparator(each), Comparison));
    }

    /// <summary>What the folder browser shows of <paramref name="path"/>: its folders, and the files <paramref name="mode"/> picks.</summary>
    public static BrowseView Browse(string? path, string mode, IEnumerable<string> recent)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var places = new List<BrowsePlace> { new("Home", home) };
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (desktop.Length > 0 && Directory.Exists(desktop)) places.Add(new("Desktop", desktop));
        places.AddRange(recent.Where(Directory.Exists).Take(4).Select(folder => new BrowsePlace(Path.GetFileName(folder), folder)));
        if (OperatingSystem.IsWindows())
            places.AddRange(DriveInfo.GetDrives().Where(drive => drive.IsReady).Select(drive => new BrowsePlace(drive.Name, drive.RootDirectory.FullName)));

        var folder = string.IsNullOrWhiteSpace(path) ? home : path.Trim();
        try
        {
            folder = Path.GetFullPath(Environment.ExpandEnvironmentVariables(folder.StartsWith('~') ? home + folder[1..] : folder));
            if (File.Exists(folder)) folder = Path.GetDirectoryName(folder)!;
            if (!Directory.Exists(folder))
                return new(folder, Parent(folder), mode, [], places, null, $"There is no folder {folder}.");
            var info = new DirectoryInfo(folder);
            var entries = info.GetDirectories()
                .Where(each => !each.Name.StartsWith('.') && !Skipped.Contains(each.Name) && !each.Attributes.HasFlag(FileAttributes.Hidden))
                .OrderBy(each => each.Name, StringComparer.OrdinalIgnoreCase)
                .Select(each => new BrowseEntry(each.Name + Path.DirectorySeparatorChar, each.FullName, true, Marker(each)))
                .ToList();
            var pattern = mode switch { "dll" => "*.dll", "yaml" => "*.y*ml", _ => null };
            if (pattern is not null)
                entries.AddRange(info.GetFiles(pattern).OrderBy(each => each.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(each => new BrowseEntry(each.Name, each.FullName, false, null)));
            var note = mode != "folder" ? null
                : TestProject.Near(folder) is { } near ? Same(Path.TrimEndingDirectorySeparator(near.Folder), Path.TrimEndingDirectorySeparator(folder))
                    ? $"The test project {near.Name} is here."
                    : $"This folder is in the test project {near.Name}, at {near.Folder}."
                : entries.Any(each => each.Detail is not null) ? "Test projects are below this folder: the recorder finds them, and their login flow."
                : "No test project here: flows are saved under this folder, and nothing logs in first.";
            return new(folder, Parent(folder), mode, entries, places, note, null);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new(folder, Parent(folder), mode, [], places, null, $"The recorder cannot look in {folder}: {failure.Message}");
        }
    }

    private static string? Marker(DirectoryInfo folder)
    {
        try
        {
            if (folder.EnumerateFiles("*.csproj").Any()) return "a project";
            if (folder.EnumerateFiles("*.sln").Any() || folder.EnumerateFiles("*.slnx").Any()) return "a solution";
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Not ours to read.
        }

        return null;
    }

    private static string? Parent(string folder) => Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder));

    private static StringComparison Comparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool Same(string one, string other) => string.Equals(one, other, Comparison);
}
