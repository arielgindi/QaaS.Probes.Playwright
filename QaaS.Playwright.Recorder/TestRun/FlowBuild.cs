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
internal sealed record BuiltFlow(string Assembly, string Symbols, string Source)
{
    /// <summary>What the compiler warned about in the flow's code, as "line 12: CS8602: …".</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Why the flow could not be built to test it, said for a person, and the build's own errors.</summary>
internal sealed class FlowBuildException(string message, string? detail = null) : Exception(message)
{
    public string? Detail { get; } = detail;
}

/// <summary>
/// Builds the flow exactly as it will be saved, as a test project builds it, with the .NET SDK, in a project of its own
/// in a temporary folder. It references the recorder's own QaaS.Playwright, Playwright and QaaS SDK as files, so
/// nothing is restored and no package feed is needed, and gives it portable symbols, so a failure has its line. It
/// compiles with the SDK's C# compiler itself, through the compiler's server, with the options, references and implicit
/// usings dotnet build would give it, as starting MSBuild takes most of a build's time; where that compiler or the
/// reference assemblies are not found, or it fails for another reason than the flow's code, dotnet build builds it. The
/// one change is a call before each statement written for a recorded action, on its first line:
/// <c>await Probe.At(n);</c>, n the statement's number. The code runs as saved and every line keeps its number, so the
/// test run is told which statement runs, and so which action (in a loop, the repeat's), may wait there (slow motion)
/// or end the flow (Run to here), and a failure's stack frame still has its line. A build is kept until the recording ends, and used again
/// when the code has not changed.
/// </summary>
/// <param name="direct">False always builds with dotnet build, as the recorder does when it cannot compile directly.</param>
internal sealed partial class FlowBuild(bool direct = true) : IDisposable
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

    // This .NET's version, "10.0", and the framework it is to a project, "net10.0".
    private static readonly string NetVersion = $"{Environment.Version.Major}.{Environment.Version.Minor}";
    private static readonly string Framework = $"net{NetVersion}";

    private static readonly string NoSdk =
        $"Testing builds the flow with the .NET {Environment.Version.Major} SDK, which is not installed here (recording " +
        "needs only the .NET runtime). Install the SDK from https://dot.net, then test again.";

    // The using directives ImplicitUsings gives a project, and the attribute the SDK gives its assembly.
    private static readonly string ImplicitUsings = $"""
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;
        [assembly: global::System.Runtime.Versioning.TargetFrameworkAttribute(".NETCoreApp,Version=v{NetVersion}", FrameworkDisplayName = ".NET {NetVersion}")]
        """;

    // Found once in the process: the compiler and the references do not change while it runs.
    private static readonly Lazy<Task<Compiler?>> Toolchain = new(FindCompilerAsync);
    private static readonly Lock WarmUpGate = new();
    private static Task? _warmUp;

    private readonly Dictionary<string, BuiltFlow> _built = [];
    private bool _direct = direct;
    private string? _folder;
    private bool _restored;

    /// <summary>Whether it compiles with the SDK's C# compiler itself: false once it could not, and builds with dotnet build.</summary>
    internal bool Direct => _direct;

    /// <summary>Builds <paramref name="flow"/>; returns the build of the same code when there is one.</summary>
    /// <exception cref="FlowBuildException">It did not build, or the .NET SDK is missing.</exception>
    /// <exception cref="OperationCanceledException">The build was cancelled.</exception>
    public async Task<BuiltFlow> BuildAsync(GeneratedFlow flow, CancellationToken cancel)
    {
        var source = Instrument(flow);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..16];
        if (_built.TryGetValue(key, out var built) && File.Exists(built.Assembly)) return built;

        var folder = await PrepareAsync(cancel);
        await File.WriteAllTextAsync(Path.Combine(folder, "Flow.cs"), source, cancel);
        var output = Path.Combine(folder, "out", key);
        built = _direct ? await CompileAsync(folder, output, quiet: false, cancel) : null;
        if (built is null)
        {
            // Once it could not, it is not tried again in this recording.
            _direct = false;
            built = await DotnetBuildAsync(folder, output, cancel);
        }

        return _built[key] = built;
    }

    /// <summary>
    /// Compiles the flow <paramref name="flow"/> gives once in the process, in the background, quietly and at a low
    /// priority, and keeps nothing of it: the compiler's server starts and reads the references, and the compiler's
    /// files are read once, so the first test compiles as fast as the next. Only a head start: whatever fails, a test
    /// says why.
    /// </summary>
    public static Task WarmUp(Func<GeneratedFlow> flow)
    {
        lock (WarmUpGate)
            return _warmUp ??= Task.Run(async () =>
            {
                try
                {
                    using var build = new FlowBuild();
                    var folder = await build.PrepareAsync(CancellationToken.None);
                    await File.WriteAllTextAsync(Path.Combine(folder, "Flow.cs"), Instrument(flow()));
                    await CompileAsync(folder, Path.Combine(folder, "out", "warm-up"), quiet: true, CancellationToken.None);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    // A test builds it again, and says what is wrong.
                }
            });
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
            ["ImplicitUsings.cs"] = ImplicitUsings,
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

    // The project built by dotnet build, the SDK's way: MSBuild, restored the first time.
    private async Task<BuiltFlow> DotnetBuildAsync(string folder, string output, CancellationToken cancel)
    {
        string[] arguments =
        [
            "build", Path.Combine(folder, $"{ProjectName}.csproj"), "--nologo", "-nodeReuse:false", "-c", "Debug",
            "-o", output, .. _restored ? (string[])["--no-restore"] : [],
        ];
        var (exitCode, log) = await DotnetAsync(folder, arguments, cancel);
        var assembly = Path.Combine(output, $"{ProjectName}.dll");
        if (exitCode != 0 || !File.Exists(assembly)) throw Failed(exitCode, log);

        _restored = true;
        return Built(folder, assembly, log);
    }

    // The project's sources compiled by the SDK's C# compiler, through its server, as dotnet build compiles them, without
    // the analyzers, which only warn. Null when it cannot for a reason of its own, not the flow's code: dotnet build
    // builds it then, and says what is wrong.
    private static async Task<BuiltFlow?> CompileAsync(string folder, string output, bool quiet, CancellationToken cancel)
    {
        if (await Toolchain.Value.WaitAsync(cancel) is not { } compiler) return null;
        Directory.CreateDirectory(output);
        var assembly = Path.Combine(output, $"{ProjectName}.dll");
        var arguments = Path.Combine(folder, "csc.rsp");
        string[] sources = ["ImplicitUsings.cs", "Flow.cs", "Probe.cs"];
        await File.WriteAllLinesAsync(arguments,
            [.. compiler.Options, $"-out:{Quoted(assembly)}", .. sources.Select(name => Quoted(Path.Combine(folder, name)))], cancel);
        int exitCode;
        string log;
        try
        {
            (exitCode, log) = await DotnetAsync(folder, ["exec", compiler.Csc, "-noconfig", "-shared", $"@{arguments}"], cancel, quiet,
                alone: true);
        }
        catch (FlowBuildException)
        {
            return null;
        }

        if (exitCode == 0 && File.Exists(assembly)) return Built(folder, assembly, log);
        if (CompilerError().IsMatch(log)) throw Failed(exitCode, log);
        return null;
    }

    private static BuiltFlow Built(string folder, string assembly, string log) =>
        new(assembly, Path.ChangeExtension(assembly, ".pdb"), Path.Combine(folder, "Flow.cs"))
        {
            Warnings =
            [
                .. CompilerWarning().Matches(log).Select(match => $"line {match.Groups["line"].Value}: {match.Groups["warning"].Value.Trim()}")
                    .Distinct(),
            ],
        };

    // The compiler of the SDK dotnet build picks for the project, the newest of this .NET's major or later, and the
    // options it gives it: those of a project of this .NET with nullable types, as Debug, with the references of this
    // .NET's newest reference pack beside it, and the recorder's own. Null when either is missing.
    private static async Task<Compiler?> FindCompilerAsync()
    {
        try
        {
            var (exitCode, sdks) = await DotnetAsync(Path.GetTempPath(), ["--list-sdks"], CancellationToken.None, quiet: true, alone: true);
            if (exitCode != 0) return null;
            var sdk = Newest(SdkLine().Matches(sdks).Select(match => (Path: match.Groups["folder"].Value, Version: match.Groups["version"].Value))
                .Where(sdk => VersionOf(sdk.Version).Number.Major >= Environment.Version.Major));
            if (sdk is not { } newest) return null;
            var (sdkFolder, sdkVersion) = newest;
            var csc = Path.Combine(sdkFolder, sdkVersion, "Roslyn", "bincore", "csc.dll");
            var packs = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sdkFolder))!, "packs", "Microsoft.NETCore.App.Ref");
            if (!File.Exists(csc) || !Directory.Exists(packs)) return null;
            var pack = Newest(Directory.GetDirectories(packs).Select(path => (Path: path, Version: Path.GetFileName(path)))
                .Where(pack => VersionOf(pack.Version).Number is var number && number.Major == Environment.Version.Major
                               && number.Minor == Environment.Version.Minor)
                .Where(pack => Directory.Exists(Path.Combine(pack.Path, "ref", Framework))));
            if (pack is null) return null;
            var framework = Directory.GetFiles(Path.Combine(pack.Value.Path, "ref", Framework), "*.dll").Order(StringComparer.Ordinal).ToList();
            if (framework.Count == 0) return null;

            var (major, minor) = (Environment.Version.Major, Environment.Version.Minor);
            string[] defines =
            [
                "TRACE", "DEBUG", "NET", $"NET{major}_{minor}", "NETCOREAPP",
                .. Enumerable.Range(5, Math.Max(0, major - 4)).Select(net => $"NET{net}_0_OR_GREATER"),
                .. ((string[])["1_0", "1_1", "2_0", "2_1", "2_2", "3_0", "3_1"]).Select(core => $"NETCOREAPP{core}_OR_GREATER"),
            ];
            string[] options =
            [
                "-nologo", "-target:library", "-debug:portable", "-optimize-", "-nullable:enable", "-deterministic",
                "-nostdlib+", "-highentropyva+", "-utf8output", "-preferreduilang:en", $"-warn:{major}",
                "-nowarn:1701,1702,8002", "-warnaserror+:SYSLIB0011", $"-langversion:{major + 4}.0",
                $"-define:{string.Join(';', defines)}",
                .. framework.Concat(References()).Select(path => $"-reference:{Quoted(path)}"),
            ];
            return new Compiler(csc, options);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // Not found where it should be: dotnet build finds it, or says why not.
            return null;
        }
    }

    // The folder of the newest version, a release before a prerelease of it; null when there is none.
    private static (string Path, string Version)? Newest(IEnumerable<(string Path, string Version)> versions) =>
        versions.Select(each => ((string Path, string Version)?)each).OrderBy(each => VersionOf(each!.Value.Version).Number)
            .ThenBy(each => VersionOf(each!.Value.Version).Release).ThenBy(each => VersionOf(each!.Value.Version).Pre, StringComparer.Ordinal)
            .LastOrDefault();

    // "10.0.300", "11.0.100-preview.3.26170.106": its number, whether it is a release, and its prerelease label with its
    // numbers padded, to order as text.
    private static (Version Number, bool Release, string Pre) VersionOf(string version)
    {
        var dash = version.IndexOf('-');
        var number = Version.TryParse(dash < 0 ? version : version[..dash], out var parsed) ? parsed : new Version();
        return (number, dash < 0, dash < 0 ? "" : Digits().Replace(version[(dash + 1)..], digits => digits.Value.PadLeft(10, '0')));
    }

    private static string Quoted(string path) => $"\"{path}\"";

    // Quiet runs it at a low priority where that leaves the compiler's server it may start as it is, on Windows: elsewhere
    // the server would keep that priority for the rest of its life. Alone, the host or the compiler itself, rather than
    // dotnet build: it writes UTF-8, and is stopped without the compiler's server it may have started, which others share.
    internal static async Task<(int ExitCode, string Log)> DotnetAsync(string folder, string[] arguments, CancellationToken cancel,
        bool quiet = false, bool alone = false)
    {
        var start = new ProcessStartInfo(Dotnet(), arguments)
        {
            WorkingDirectory = folder, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            CreateNoWindow = true, StandardOutputEncoding = alone ? Encoding.UTF8 : null, StandardErrorEncoding = alone ? Encoding.UTF8 : null,
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
            if (quiet && OperatingSystem.IsWindows()) LowerPriority(process);
            var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
            try
            {
                await process.WaitForExitAsync(cancel);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: !alone);
                throw;
            }

            return (process.ExitCode, await output + await errors);
        }
    }

    private static void LowerPriority(Process process)
    {
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception failure) when (failure is InvalidOperationException or Win32Exception)
        {
            // It ended already, or may not be changed: it runs as it is.
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

    // "/tmp/.../Flow.cs(31,9): error CS0103: The name 'x' does not exist in the current context [/tmp/.../x.csproj]", as
    // dotnet build says it; the compiler says it without the project.
    [GeneratedRegex(@"Flow\.cs\((?<line>\d+),\d+\): error (?<error>[^\[\r\n]+)")]
    private static partial Regex CompilerError();

    [GeneratedRegex(@"Flow\.cs\((?<line>\d+),\d+\): warning (?<warning>[^\[\r\n]+)")]
    private static partial Regex CompilerWarning();

    // "10.0.300 [/usr/share/dotnet/sdk]", as dotnet --list-sdks lists each SDK.
    [GeneratedRegex(@"^(?<version>\d+\.\d+\.\d+\S*) \[(?<folder>.+)\]\r?$", RegexOptions.Multiline)]
    private static partial Regex SdkLine();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();

    /// <summary>The SDK's C# compiler, csc.dll, and the options that compile the flow as dotnet build would.</summary>
    private sealed record Compiler(string Csc, IReadOnlyList<string> Options);
}
