using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;

namespace QaaS.Playwright.Recorder.Login;

/// <summary>A build of the test project: the DLL in its bin folder, and when it was built.</summary>
internal sealed record ProjectBuild(string Dll, DateTime BuiltAt)
{
    /// <summary>Debug or Release, as the folder it is in says.</summary>
    public string Configuration => new DirectoryInfo(Path.GetDirectoryName(Dll)!).Parent?.Name ?? "Debug";
}

/// <summary>A YAML file of the tests, read as QaaS reads a test's, and the flows it runs as setup flows.</summary>
internal sealed record TestFile(string Path, IConfiguration Yaml, IReadOnlySet<string> SetupFlows);

/// <summary>
/// The test project the recorder runs in: the nearest .csproj from a folder up, the newest build of it, and its YAML
/// files, as a person would find them.
/// </summary>
internal sealed record TestProject(string ProjectFile, string AssemblyName)
{
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
                return new TestProject(project.FullName, AssemblyNameOf(project));
        }

        return null;
    }

    /// <summary>The newest <c>bin/&lt;Debug|Release&gt;/&lt;tfm&gt;/&lt;AssemblyName&gt;.dll</c>; null when it is not built.</summary>
    public ProjectBuild? NewestBuild()
    {
        var bin = new DirectoryInfo(Path.Combine(Folder, "bin"));
        if (!bin.Exists) return null;
        var dll = $"{AssemblyName}.dll";
        try
        {
            return bin.EnumerateDirectories().SelectMany(configuration => configuration.EnumerateDirectories())
                .SelectMany(framework => (DirectoryInfo[])[framework, .. framework.EnumerateDirectories()])
                .Select(folder => new FileInfo(Path.Combine(folder.FullName, dll)))
                .Where(file => file.Exists)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => new ProjectBuild(file.FullName, file.LastWriteTimeUtc))
                .FirstOrDefault();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
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
        FileInfo[] files;
        DirectoryInfo[] folders;
        try
        {
            (files, folders) = (folder.GetFiles(), folder.GetDirectories());
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files) yield return file;
        foreach (var inner in folders.Where(inner => !NotLooked.Contains(inner.Name) && !inner.Name.StartsWith('.')))
        foreach (var file in FilesUnder(inner))
            yield return file;
    }

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
