using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;

namespace QaaS.Playwright.Recorder.Login;

/// <summary>
/// A build of the test project: its DLL, when it was built, its configuration, and how it was found (in its bin folder,
/// or where MSBuild says it builds to).
/// </summary>
internal sealed record ProjectBuild(string Dll, DateTime BuiltAt, string Configuration, string How);

/// <summary>A YAML file of the tests, read as QaaS reads a test's, and the flows it runs as setup flows.</summary>
internal sealed record TestFile(string Path, IConfiguration Yaml, IReadOnlySet<string> SetupFlows);

/// <summary>
/// The test project the recorder runs in: found from a folder up, else below it or in its solution; the newest build of
/// it, where MSBuild builds it; and its YAML files, as a person would find them.
/// </summary>
internal sealed partial record TestProject(string ProjectFile, string AssemblyName)
{
    /// <summary>How many folders below the one the recorder runs in it looks for a test project.</summary>
    public const int Depth = 3;

    private static readonly HashSet<string> NotLooked = new(["bin", "obj", "node_modules"], StringComparer.OrdinalIgnoreCase);

    public string Folder => Path.GetDirectoryName(ProjectFile)!;

    public string Name => Path.GetFileNameWithoutExtension(ProjectFile);

    /// <summary>The project of the nearest .csproj in <paramref name="folder"/> or a folder above it; null when none.</summary>
    public static TestProject? Near(string folder)
    {
        for (var ancestor = new DirectoryInfo(Path.GetFullPath(folder)); ancestor is not null; ancestor = ancestor.Parent)
        {
            // The output folder is made when the flow is saved, so it may not be there yet.
            if (!ancestor.Exists) continue;
            if (ancestor.GetFiles("*.csproj").OrderBy(file => file.Name, StringComparer.Ordinal).FirstOrDefault() is { } project)
                return Of(project.FullName);
        }

        return null;
    }

    /// <summary>The project of <paramref name="projectFile"/>.</summary>
    public static TestProject Of(string projectFile) => new(Path.GetFullPath(projectFile), AssemblyNameOf(new FileInfo(projectFile)));

    /// <summary>
    /// The projects up to <see cref="Depth"/> folders below <paramref name="folder"/>, and those of its .sln or .slnx files,
    /// each once, and where each was found; bin, obj, node_modules and hidden folders are skipped.
    /// </summary>
    public static List<(TestProject Project, string Where)> Below(string folder)
    {
        var found = new Dictionary<string, (TestProject, string)>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);
        var root = new DirectoryInfo(Path.GetFullPath(folder));
        if (!root.Exists) return [];
        foreach (var solution in Files(root, "*.sln").Concat(Files(root, "*.slnx")))
        foreach (var project in ProjectsOf(solution))
            found.TryAdd(project, (Of(project), $"in {solution.Name}"));

        var level = new List<DirectoryInfo> { root };
        for (var depth = 0; depth <= Depth && level.Count > 0; depth++)
        {
            var next = new List<DirectoryInfo>();
            foreach (var at in level)
            {
                foreach (var project in Files(at, "*.csproj"))
                    found.TryAdd(project.FullName, (Of(project.FullName), depth == 0 ? "here" : "below"));
                if (depth < Depth) next.AddRange(Folders(at).Where(inner => !NotLooked.Contains(inner.Name) && !inner.Name.StartsWith('.')));
            }

            level = next;
        }

        return [.. found.Values.OrderBy(entry => entry.Item1.ProjectFile, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Why the project is one of QaaS tests, or null when it is not: a YAML file of it runs a setup flow, the surest sign;
    /// else its restore took a QaaS package (QaaS.Framework.*, QaaS.Playwright), as a shared package of the team's brings
    /// them. <paramref name="runsSetupFlows"/> tells which.
    /// </summary>
    public string? UsesQaaS(out bool runsSetupFlows)
    {
        runsSetupFlows = Look().Files.Any(file => file.SetupFlows.Count > 0);
        if (runsSetupFlows) return "a YAML file of it runs SetupFlows";
        foreach (var assets in AssetsFiles())
        {
            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(assets));
                if (json.RootElement.TryGetProperty("libraries", out var libraries)
                    && libraries.EnumerateObject().FirstOrDefault(library => IsQaaSPackage(library)) is { Name: { Length: > 0 } name })
                    return $"its restore took {name.Split('/')[0]} ({ConsoleUi.Relative(assets)})";
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
                                                or InvalidOperationException)
            {
                // Not restored, or not readable.
            }
        }

        return null;
    }

    private static bool IsQaaSPackage(System.Text.Json.JsonProperty library) =>
        (library.Name.StartsWith("QaaS.Framework", StringComparison.OrdinalIgnoreCase)
         || library.Name.StartsWith("QaaS.Playwright/", StringComparison.OrdinalIgnoreCase))
        && library.Value.TryGetProperty("type", out var type) && type.GetString() == "package";

    /// <summary>
    /// Where the project's restore wrote what it took: <c>obj/project.assets.json</c>, or under an <c>artifacts</c> folder
    /// above it, as <c>UseArtifactsOutput</c> has it; those that are there.
    /// </summary>
    public IEnumerable<string> AssetsFiles()
    {
        var own = Path.Combine(Folder, "obj", "project.assets.json");
        if (File.Exists(own)) yield return own;
        for (var ancestor = new DirectoryInfo(Folder); ancestor is not null; ancestor = ancestor.Parent)
        {
            var artifacts = Path.Combine(ancestor.FullName, "artifacts", "obj", Name, "project.assets.json");
            if (File.Exists(artifacts)) yield return artifacts;
        }
    }

    /// <summary>The newest <c>bin/&lt;Debug|Release&gt;/&lt;tfm&gt;/&lt;AssemblyName&gt;.dll</c>; null when there is none.</summary>
    public ProjectBuild? NewestInBin()
    {
        var bin = new DirectoryInfo(Path.Combine(Folder, "bin"));
        if (!bin.Exists) return null;
        var dll = $"{AssemblyName}.dll";
        try
        {
            return bin.EnumerateDirectories().SelectMany(configuration => configuration.EnumerateDirectories()
                    .SelectMany(framework => (DirectoryInfo[])[framework, .. framework.EnumerateDirectories()])
                    .Select(folder => (Configuration: configuration.Name, File: new FileInfo(Path.Combine(folder.FullName, dll)))))
                .Where(found => found.File.Exists)
                .OrderByDescending(found => found.File.LastWriteTimeUtc)
                .Select(found => new ProjectBuild(found.File.FullName, found.File.LastWriteTimeUtc, found.Configuration,
                    $"in its bin folder ({found.Configuration})"))
                .FirstOrDefault();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The newest build: the one in its bin folder, else, when there is none there or it is older than
    /// <paramref name="changed"/>, the one where MSBuild says the project builds to (its <c>TargetPath</c>, in Debug and
    /// Release), as a <c>Directory.Build.props</c> or <c>UseArtifactsOutput</c> moves it. Null when it is not built.
    /// <paramref name="search"/> is told what was looked at.
    /// </summary>
    public ProjectBuild? NewestBuild(DateTime changed = default, LoginSearch? search = null)
    {
        var inBin = NewestInBin();
        if (inBin is not null && inBin.BuiltAt >= changed)
        {
            search?.Say($"Build: {ConsoleUi.Relative(inBin.Dll)}, {inBin.How}, built {ConsoleUi.Ago(inBin.BuiltAt.ToLocalTime())}.", 1);
            return inBin;
        }

        search?.Say(inBin is null
            ? $"Build: none in {ConsoleUi.Relative(Path.Combine(Folder, "bin"))}; asking MSBuild where it builds to."
            : $"Build: {ConsoleUi.Relative(inBin.Dll)} is older than the project; asking MSBuild where it builds to.", 1);
        var targets = ProjectBuilds.Of(this, search);
        var elsewhere = targets.Targets
            .Select(target => (target.Key, File: new FileInfo(target.Value)))
            .Where(target => target.File.Exists)
            .OrderByDescending(target => target.File.LastWriteTimeUtc)
            .Select(target => new ProjectBuild(target.File.FullName, target.File.LastWriteTimeUtc, target.Key,
                $"where MSBuild builds it ({target.Key})"))
            .FirstOrDefault();
        foreach (var (configuration, dll) in targets.Targets)
            search?.Say($"{configuration}: {ConsoleUi.Relative(dll)}{(File.Exists(dll) ? "" : ", not built")}", 2);
        var newest = elsewhere is not null && (inBin is null || elsewhere.BuiltAt > inBin.BuiltAt) ? elsewhere : inBin;
        if (newest is not null)
            search?.Say($"Taken: {ConsoleUi.Relative(newest.Dll)}, built {ConsoleUi.Ago(newest.BuiltAt.ToLocalTime())}.", 2);
        return newest;
    }

    /// <summary>
    /// The project's YAML files that run a setup flow or set a flow's values, and when its code and its project files
    /// last changed; bin, obj and hidden folders are skipped.
    /// </summary>
    public (IReadOnlyList<TestFile> Files, DateTime CodeChanged, DateTime ProjectChanged) Look()
    {
        var files = new List<TestFile>();
        DateTime code = DateTime.MinValue, project = DateTime.MinValue;
        foreach (var path in FilesUnder(new DirectoryInfo(Folder)))
        {
            switch (Path.GetExtension(path.Name).ToLowerInvariant())
            {
                case ".cs":
                    code = Max(code, path.LastWriteTimeUtc);
                    break;
                case ".csproj" or ".props" or ".targets":
                    project = Max(project, path.LastWriteTimeUtc);
                    break;
                case ".yaml" or ".yml" when Read(path.FullName) is { } file:
                    files.Add(file);
                    break;
            }
        }

        return (files, code, project);
    }

    private static DateTime Max(DateTime one, DateTime other) => one > other ? one : other;

    // Only a file that names a setup flow or a flow's values is read as YAML: a test project has many others.
    private static TestFile? Read(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            if (!text.Contains("SetupFlows", StringComparison.OrdinalIgnoreCase)
                && !text.Contains("FlowConfiguration", StringComparison.OrdinalIgnoreCase))
                return null;
            var yaml = LoginSettings.Open(path);
            return new TestFile(path, yaml, SetupFlowsIn(yaml).ToHashSet(StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null; // Not a test the probe could run either.
        }
    }

    // "SetupFlows: [LoginFlow]" anywhere, a list or a single name.
    private static IEnumerable<string> SetupFlowsIn(IConfiguration node)
    {
        foreach (var child in node.GetChildren())
        {
            if (!child.Key.Equals("SetupFlows", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var deeper in SetupFlowsIn(child)) yield return deeper;
                continue;
            }

            if (child.Value is { Length: > 0 } single) yield return single.Trim();
            foreach (var item in child.GetChildren())
                if (item.Value is { Length: > 0 } name)
                    yield return name.Trim();
        }
    }

    private static IEnumerable<FileInfo> FilesUnder(DirectoryInfo folder)
    {
        foreach (var file in Files(folder, "*")) yield return file;
        foreach (var inner in Folders(folder).Where(inner => !NotLooked.Contains(inner.Name) && !inner.Name.StartsWith('.')))
        foreach (var file in FilesUnder(inner))
            yield return file;
    }

    private static FileInfo[] Files(DirectoryInfo folder, string pattern)
    {
        try
        {
            return folder.GetFiles(pattern);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static DirectoryInfo[] Folders(DirectoryInfo folder)
    {
        try
        {
            return folder.GetDirectories();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // The C# projects a solution lists, that are there: a .sln's Project(...) lines, a .slnx's <Project Path="..."/>.
    private static IEnumerable<string> ProjectsOf(FileInfo solution)
    {
        IEnumerable<string> paths;
        try
        {
            paths = solution.Extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
                ? XDocument.Load(solution.FullName).Descendants("Project").Select(project => (string?)project.Attribute("Path")).OfType<string>()
                : SolutionProject().Matches(File.ReadAllText(solution.FullName)).Select(match => match.Groups[1].Value);
            paths = [.. paths];
        }
        catch (Exception failure) when (failure is XmlException or IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var path in paths.Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            var full = Path.GetFullPath(Path.Combine(solution.DirectoryName!, path.Replace('\\', Path.DirectorySeparatorChar)));
            if (File.Exists(full)) yield return full;
        }
    }

    [GeneratedRegex("""^Project\("[^"]*"\)\s*=\s*"[^"]*",\s*"([^"]+)"\s*,""", RegexOptions.Multiline)]
    private static partial Regex SolutionProject();

    private static string AssemblyNameOf(FileInfo project)
    {
        var name = Path.GetFileNameWithoutExtension(project.Name);
        try
        {
            var given = XDocument.Load(project.FullName).Descendants("AssemblyName").FirstOrDefault()?.Value.Trim();
            given = given?.Replace("$(MSBuildProjectName)", name, StringComparison.OrdinalIgnoreCase);
            return given is { Length: > 0 } && !given.Contains("$(", StringComparison.Ordinal) ? given : name;
        }
        catch (Exception failure) when (failure is XmlException or IOException or UnauthorizedAccessException)
        {
            return name;
        }
    }
}
