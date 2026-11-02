using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>
/// Builds recorded flows the way a test project does: a project with nullable types, implicit usings and the repo's
/// code style enforced, every warning an error, referencing this build's QaaS.Playwright and Playwright.
/// </summary>
public static class FlowCompiler
{
    private static readonly string[] References =
    [
        "QaaS.Playwright.dll", "Microsoft.Playwright.dll", "QaaS.Framework.SDK.dll",
        "Microsoft.Extensions.Configuration.Abstractions.dll", "Microsoft.Extensions.Logging.Abstractions.dll",
        // Playwright's IPage is IAsyncDisposable through it, which a flow that keeps a page in a variable needs.
        "Microsoft.Bcl.AsyncInterfaces.dll",
    ];

    /// <summary>The folder a project keeps its flows in; the recorder names their namespace after it.</summary>
    public static string FlowsFolder(string projectFolder) => Path.Combine(projectFolder, "Flows");

    /// <summary>Writes a test project in <paramref name="projectFolder"/>, for the recorder to save flows into.</summary>
    public static async Task CreateProjectAsync(string projectFolder, string assemblyName)
    {
        var references = string.Join("\n", References.Select(name =>
            $"""    <Reference Include="{Path.Combine(AppContext.BaseDirectory, name)}" Private="false" />"""));
        await File.WriteAllTextAsync(Path.Combine(projectFolder, $"{assemblyName}.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <RootNamespace>GeneratedFlows</RootNamespace>
                <AssemblyName>{assemblyName}</AssemblyName>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
                <GenerateDocumentationFile>true</GenerateDocumentationFile>
                <NoWarn>$(NoWarn);CS1591</NoWarn>
                <!-- The packages' own version conflict, which a project with package references resolves. -->
                <MSBuildWarningsAsMessages>MSB3277</MSBuildWarningsAsMessages>
              </PropertyGroup>
              <ItemGroup>
            {references}
              </ItemGroup>
            </Project>
            """);
        File.Copy(Path.Combine(RepositoryRoot(), ".editorconfig"), Path.Combine(projectFolder, ".editorconfig"), overwrite: true);
        Directory.CreateDirectory(FlowsFolder(projectFolder));
    }

    /// <summary>Builds the project. Fails the test with the build's output when it does not build without warnings.</summary>
    public static async Task BuildAsync(string projectFolder)
    {
        var output = Path.Combine(projectFolder, "out");
        var build = new ProcessStartInfo("dotnet", ["build", "--nologo", "-o", output, projectFolder])
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            Environment = { ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["DOTNET_NOLOGO"] = "1" },
        };
        using var process = Process.Start(build)!;
        var log = await process.StandardOutput.ReadToEndAsync() + await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.That(process.ExitCode, Is.Zero, $"The recorded flow does not build:\n{log}");
        Assert.That(log, Does.Contain(" 0 Warning(s)"), $"The recorded flow builds with warnings:\n{log}");
    }

    private static string RepositoryRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
