using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Playwright.Recorder.Generation;

namespace QaaS.Playwright.Recorder.TestRun;

/// <summary>A test run's build of the flow: its assembly, its symbols, and the source file the symbols name.</summary>
internal sealed record BuiltFlow(string Assembly, string Symbols, string Source);

/// <summary>Why the flow could not be built to test it, said for a person, and the build's own errors.</summary>
internal sealed class FlowBuildException(string message, string? detail = null) : Exception(message)
{
    public string? Detail { get; } = detail;
}

/// <summary>
/// Builds the flow exactly as it will be saved, as a test project builds it, with the .NET SDK, in a project of its own
/// in a temporary folder. It references the recorder's own QaaS.Playwright, Playwright and QaaS SDK as files, so
/// nothing is restored and no package feed is needed, and gives it portable symbols, so a failure has its line. The
/// one change is a call before each statement written for a recorded action, on its first line:
/// <c>await Probe.At(n);</c>, n the statement's number. The code runs as saved and every line keeps its number, so the
/// test run is told which statement runs, and so which action (in a loop, the repeat's), may wait there (slow motion)
/// or end the flow (Run to here), and a failure's stack frame still has its line. A build is kept until the recording ends, and used again
/// when the code has not changed.
/// </summary>
internal sealed partial class FlowBuild : IDisposable
{
    /// <summary>The class the test build's code calls as each action begins, by its full name.</summary>
    public const string ProbeType = "QaasRecorderTestRun.Probe";

    private const string ProjectName = "QaaS.Recorder.TestRun";
    private const string ProbeCall = "await global::" + ProbeType + ".At";

    private const string ProbeSource = """
        namespace QaasRecorderTestRun;

        /// <summary>Tells the recorder's test run, as each action of the flow begins, which one it is.</summary>
        public static class Probe
        {
            /// <summary>Set by the recorder for the run; it may wait, or end the flow there by throwing.</summary>
            public static System.Func<int, System.Threading.Tasks.Task>? Reached;

            /// <summary>The statement numbered <paramref name="statement"/> begins.</summary>
            public static System.Threading.Tasks.Task At(int statement) =>
                Reached?.Invoke(statement) ?? System.Threading.Tasks.Task.CompletedTask;
        }
        """;

    private static readonly string Framework = $"net{Environment.Version.Major}.{Environment.Version.Minor}";

    private static readonly string NoSdk =
        $"Testing builds the flow with the .NET {Environment.Version.Major} SDK, which is not installed here (recording " +
        "needs only the .NET runtime). Install the SDK from https://dot.net, then test again.";

    private readonly Dictionary<string, BuiltFlow> _built = [];
    private string? _folder;
    private bool _restored;

    /// <summary>Builds <paramref name="flow"/>; returns the build of the same code when there is one.</summary>
    /// <exception cref="FlowBuildException">It did not build, or the .NET SDK is missing.</exception>
    /// <exception cref="OperationCanceledException">The build was cancelled.</exception>
    public async Task<BuiltFlow> BuildAsync(GeneratedFlow flow, CancellationToken cancel)
    {
        var source = Instrument(flow);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..16];
        if (_built.TryGetValue(key, out var built) && File.Exists(built.Assembly)) return built;

        var folder = await PrepareAsync(cancel);
        var sourcePath = Path.Combine(folder, "Flow.cs");
        await File.WriteAllTextAsync(sourcePath, source, cancel);
        var output = Path.Combine(folder, "out", key);
        string[] arguments =
        [
            "build", Path.Combine(folder, $"{ProjectName}.csproj"), "--nologo", "-nodeReuse:false", "-c", "Debug",
            "-o", output, .. _restored ? (string[])["--no-restore"] : [],
        ];
        var (exitCode, log) = await DotnetAsync(folder, arguments, cancel);
        var assembly = Path.Combine(output, $"{ProjectName}.dll");
        if (exitCode != 0 || !File.Exists(assembly)) throw Failed(exitCode, log);

        _restored = true;
        return _built[key] = new BuiltFlow(assembly, Path.ChangeExtension(assembly, ".pdb"), sourcePath);
    }

    /// <summary>Writes the flow's settings, its recorded values, for the run to read as the probe reads its YAML.</summary>
    public async Task<string> WriteSettingsAsync(GeneratedFlow flow, CancellationToken cancel)
    {
        var path = Path.Combine(await PrepareAsync(cancel), "Flow.flow.yaml");
        await File.WriteAllTextAsync(path, flow.Yaml, cancel);
        return path;
    }

    /// <summary>
    /// A copy of the login at <paramref name="path"/>, which the recording started from, for the run to start from too,
    /// as the probe starts from a LoadStorageStatePath saved for it; null when there is none.
    /// </summary>
    public async Task<string?> CopyLoginAsync(string? path, CancellationToken cancel)
    {
        if (path is null || !File.Exists(path)) return null;
        var copy = Path.Combine(await PrepareAsync(cancel), "login.json");
        await File.WriteAllBytesAsync(copy, await File.ReadAllBytesAsync(path, cancel), cancel);
        return copy;
    }

    public void Dispose()
    {
        try
        {
            if (_folder is not null && Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // A file still in use: the system cleans its temporary folder.
        }
    }

    /// <summary>
    /// The flow's code with <c>await Probe.At(n);</c> before each of its <see cref="GeneratedFlow.Statements"/>, on its
    /// first line. A statement that declares nothing goes in braces with it, so a loop's one statement stays all its
    /// body; the closing brace ends its last line, before the mark of a check.
    /// </summary>
    internal static string Instrument(GeneratedFlow flow)
    {
        var lines = flow.Code.Split('\n');
        for (var number = 0; number < flow.Statements.Count; number++)
        {
            var (first, last) = (flow.Statements[number].Lines.First - 1, flow.Statements[number].Lines.Last - 1);
            var line = lines[first];
            var indent = line.Length - line.TrimStart().Length;
            var declares = line[indent..].StartsWith("var ", StringComparison.Ordinal);
            lines[first] = $"{line[..indent]}{(declares ? "" : "{ ")}{ProbeCall}({number}); {line[indent..]}";
            if (declares) continue;
            var end = lines[last];
            var mark = new[] { FlowWriter.AutoCheck, FlowWriter.ChosenCheck }.FirstOrDefault(mark => end.EndsWith(mark, StringComparison.Ordinal));
            lines[last] = mark is null ? $"{end} }}" : $"{end[..^mark.Length]} }}{mark}";
        }

        return string.Join('\n', lines);
    }

    // The project, once, in a folder only the person can read, as it holds the values they typed: outside any
    // repository's Directory.Build.props, package versions and feeds, on an SDK of this .NET or later, referencing what
    // the recorder runs on, as files.
    private async Task<string> PrepareAsync(CancellationToken cancel)
    {
        if (_folder is not null) return _folder;
        var folder = Directory.CreateTempSubdirectory("qaas-recorder-test-").FullName;
        var references = string.Join("\n", References().Select(path =>
            $"""    <Reference Include="{SecurityElement.Escape(path)}" Private="false" />"""));
        Dictionary<string, string> files = new()
        {
            [$"{ProjectName}.csproj"] = $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>{Framework}</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <AssemblyName>{ProjectName}</AssemblyName>
                    <EnableDefaultItems>false</EnableDefaultItems>
                    <DebugType>portable</DebugType>
                    <GenerateDependencyFile>false</GenerateDependencyFile>
                    <ProduceReferenceAssembly>false</ProduceReferenceAssembly>
                    <SatelliteResourceLanguages>en</SatelliteResourceLanguages>
                    <NuGetAudit>false</NuGetAudit>
                    <!-- The packages' own version conflict, which a project with package references resolves. -->
                    <MSBuildWarningsAsMessages>MSB3277</MSBuildWarningsAsMessages>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="Flow.cs" />
                    <Compile Include="Probe.cs" />
                {references}
                  </ItemGroup>
                </Project>
                """,
            ["Probe.cs"] = ProbeSource,
            ["global.json"] = $$"""{ "sdk": { "version": "{{Environment.Version.Major}}.0.100", "rollForward": "latestMajor" } }""",
            ["nuget.config"] = """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                  </packageSources>
                </configuration>
                """,
            ["Directory.Build.props"] = "<Project />",
            ["Directory.Build.targets"] = "<Project />",
            ["Directory.Packages.props"] = "<Project />",
        };
        foreach (var (name, text) in files) await File.WriteAllTextAsync(Path.Combine(folder, name), text, cancel);
        return _folder = folder;
    }

    // What a flow's code needs to build, as FlowCompiler gives a test project: the recorder's own files of them.
    private static IEnumerable<string> References()
    {
        Type[] types = [typeof(BasePlaywrightFlow<>), typeof(IPage), typeof(Context), typeof(IConfiguration), typeof(ILogger)];
        foreach (var location in types.Select(type => type.Assembly.Location).Distinct()) yield return location;
        // Playwright's IPage is IAsyncDisposable through it, which a flow that keeps a page in a variable needs.
        var asyncInterfaces = Path.Combine(AppContext.BaseDirectory, "Microsoft.Bcl.AsyncInterfaces.dll");
        if (File.Exists(asyncInterfaces)) yield return asyncInterfaces;
    }

    internal static async Task<(int ExitCode, string Log)> DotnetAsync(string folder, string[] arguments, CancellationToken cancel)
    {
        var start = new ProcessStartInfo(Dotnet(), arguments)
        {
            WorkingDirectory = folder, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        // Set when the recorder itself runs from an SDK command, e.g. dotnet run: they would mix two SDKs.
        foreach (var inherited in (string[])["MSBuildExtensionsPath", "MSBuildSDKsPath", "MSBUILD_EXE_PATH", "MSBuildLoadMicrosoftTargetsReadOnly"])
            start.Environment.Remove(inherited);
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new FlowBuildException(NoSdk);
        }
        catch (Win32Exception)
        {
            throw new FlowBuildException(NoSdk);
        }

        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
            try
            {
                await process.WaitForExitAsync(cancel);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            return (process.ExitCode, await output + await errors);
        }
    }

    // The dotnet that runs the recorder, whose SDKs build for its .NET; else the one on the PATH.
    private static string Dotnet()
    {
        var root = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        var host = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return File.Exists(host) ? host : "dotnet";
    }

    private static FlowBuildException Failed(int exitCode, string log)
    {
        string[] noSdk =
        [
            "No .NET SDKs were found", "compatible .NET SDK was not found", "NETSDK1045", "SDK 'Microsoft.NET.Sdk'",
            "You intended to execute a .NET SDK command",
        ];
        if (noSdk.Any(sign => log.Contains(sign, StringComparison.Ordinal))) return new FlowBuildException(NoSdk, log.Trim());
        var errors = CompilerError().Matches(log).Select(match => $"line {match.Groups["line"].Value}: {match.Groups["error"].Value.Trim()}")
            .Distinct().ToList();
        return errors.Count > 0
            ? new FlowBuildException($"The flow does not build, {errors[0]}", string.Join('\n', errors))
            : new FlowBuildException($"The flow did not build: dotnet build exited with {exitCode}.", log.Trim());
    }

    // "/tmp/.../Flow.cs(31,9): error CS0103: The name 'x' does not exist in the current context [/tmp/.../x.csproj]"
    [GeneratedRegex(@"Flow\.cs\((?<line>\d+),\d+\): error (?<error>[^\[\r\n]+)")]
    private static partial Regex CompilerError();
}
