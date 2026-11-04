using System.Globalization;
using System.Text.Json;
using QaaS.Playwright.Browser;
using QaaS.Playwright.Recorder.TestRun;

namespace QaaS.Playwright.Recorder.Login;

/// <summary>
/// Where a project builds to, as MSBuild says it (<c>dotnet msbuild -getProperty:TargetPath</c>, in Debug and Release):
/// the place a <c>Directory.Build.props</c> with <c>OutputPath</c> or <c>BaseOutputPath</c>, or <c>UseArtifactsOutput</c>,
/// moves its DLL to; and where its restore wrote what it took. Asked once, and kept in
/// <c>~/.qaas/recorder-builds.json</c> until the project, or a <c>Directory.Build.props</c> or <c>.targets</c> above it,
/// changes, as MSBuild takes a second or two to say it.
/// </summary>
internal static class ProjectBuilds
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] Configurations = ["Debug", "Release"];

    /// <summary>What MSBuild says of a project: its DLL in each configuration, and its restore's assets file.</summary>
    internal sealed record Targets(string Stamp, Dictionary<string, string> Dlls, string? Assets);

    /// <summary>Kept beside the logins remembered: <c>~/.qaas/recorder-builds.json</c>.</summary>
    private static string FilePath => Path.Combine(Path.GetDirectoryName(RememberedLogin.FilePath)!, "recorder-builds.json");

    /// <summary>Where <paramref name="project"/> builds to in Debug and Release; none when MSBuild could not say.</summary>
    public static (Dictionary<string, string> Targets, string? Assets) Of(TestProject project, LoginSearch? search = null)
    {
        var stamp = StampOf(project);
        var kept = Read();
        if (kept.TryGetValue(project.ProjectFile, out var known) && known.Stamp == stamp)
        {
            search?.Say("MSBuild said it before, and the project has not changed since:", 2);
            return (known.Dlls, known.Assets);
        }

        var asked = Configurations.Select(configuration => AskAsync(project, configuration)).ToArray();
        Task.WaitAll(asked);
        var dlls = new Dictionary<string, string>();
        string? assets = null, failed = null;
        foreach (var (configuration, (dll, assetsFile, why)) in Configurations.Zip(asked.Select(task => task.Result)))
        {
            if (dll is not null) dlls[configuration] = dll;
            assets ??= assetsFile;
            failed ??= why;
        }

        search?.Say(dlls.Count > 0 ? "MSBuild says:" : $"MSBuild could not say: {failed ?? "it gave no TargetPath"}", 2);
        if (dlls.Count == 0) return (dlls, null);
        kept[project.ProjectFile] = new Targets(stamp, dlls, assets);
        try
        {
            AtomicFileWriter.WriteAsync(FilePath, JsonSerializer.Serialize(kept, Json)).GetAwaiter().GetResult();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Asked again next time.
        }

        return (dlls, assets);
    }

    /// <summary>The restore's assets file MSBuild said the project has, when it said so and nothing changed since; else null.</summary>
    public static string? AssetsOf(TestProject project) =>
        Read().TryGetValue(project.ProjectFile, out var known) && known.Stamp == StampOf(project) ? known.Assets : null;

    // The project's TargetPath and assets file in one configuration; with several frameworks, in this .NET's, else the
    // first. A reason instead when MSBuild failed.
    private static async Task<(string? Dll, string? Assets, string? Why)> AskAsync(TestProject project, string configuration)
    {
        var properties = await PropertiesAsync(project, configuration, null);
        if (properties is not { } found) return (null, null, "dotnet msbuild failed");
        if (found.TargetPath is { Length: > 0 } single) return (single, found.Assets, null);
        var frameworks = (found.Frameworks ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (frameworks.Length == 0) return (null, null, "it gave no TargetPath");
        var own = $"net{Environment.Version.Major}.{Environment.Version.Minor}";
        var framework = frameworks.FirstOrDefault(name => name.Equals(own, StringComparison.OrdinalIgnoreCase)) ?? frameworks[0];
        var inner = await PropertiesAsync(project, configuration, framework);
        return inner?.TargetPath is { Length: > 0 } dll ? (dll, inner.Value.Assets ?? found.Assets, null) : (null, null, "it gave no TargetPath");
    }

    private static async Task<(string? TargetPath, string? Frameworks, string? Assets)?> PropertiesAsync(TestProject project,
        string configuration, string? framework)
    {
        string[] arguments =
        [
            "msbuild", project.ProjectFile, "-nologo", "-getProperty:TargetPath", "-getProperty:TargetFrameworks",
            "-getProperty:ProjectAssetsFile", $"-p:Configuration={configuration}",
            .. framework is null ? (string[])[] : [$"-p:TargetFramework={framework}"],
        ];
        try
        {
            var (exitCode, output) = await FlowBuild.DotnetAsync(project.Folder, arguments, CancellationToken.None);
            if (exitCode != 0) return null;
            var start = output.IndexOf('{');
            if (start < 0) return null;
            using var json = JsonDocument.Parse(output[start..]);
            var found = json.RootElement.GetProperty("Properties");
            string? Of(string name) => found.TryGetProperty(name, out var value) && value.GetString() is { Length: > 0 } text ? text : null;
            return (Of("TargetPath"), Of("TargetFrameworks"), Of("ProjectAssetsFile"));
        }
        catch (Exception failure) when (failure is FlowBuildException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    // When the project, and the Directory.Build.props and .targets of its folder and those above it, last changed.
    private static string StampOf(TestProject project)
    {
        var files = new List<string> { project.ProjectFile };
        for (var folder = new DirectoryInfo(project.Folder); folder is not null; folder = folder.Parent)
            files.AddRange(((string[])["Directory.Build.props", "Directory.Build.targets"])
                .Select(name => Path.Combine(folder.FullName, name)).Where(File.Exists));
        return string.Join(";", files.Select(file =>
            $"{file}@{File.GetLastWriteTimeUtc(file).Ticks.ToString(CultureInfo.InvariantCulture)}"));
    }

    private static Dictionary<string, Targets> Read()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Dictionary<string, Targets>>(File.ReadAllText(FilePath), Json) ?? []
                : [];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}
