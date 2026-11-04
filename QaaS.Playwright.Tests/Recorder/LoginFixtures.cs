using System.Security.Cryptography;
using System.Text;
using QaaS.Playwright.Recorder.TestRun;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>
/// Repositories a person records in, built once and kept in the temporary folder until their files change:
/// <list type="bullet">
/// <item><c>repo</c>: a test project two folders down, in a .slnx with a shared library that has its login flow and a
/// tool that is no QaaS project, built to a folder of its own by a <c>Directory.Build.props</c>.</item>
/// <item><c>pkg</c>: two test projects. One references the shared login as a package from a local feed, which its
/// build does not copy beside it; the other a login flow built against an older QaaS.Playwright (1.15.1, a stand-in)
/// that has <c>BrowserDefaults.RemoteUrl</c>, which this one has not.</item>
/// </list>
/// </summary>
public static class LoginFixtures
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    // This build's assemblies, referenced as files, as the recorder's tests build flows.
    private static string References(params string[] names) => string.Join("\n", names.Select(name =>
        $"""    <Reference Include="{Path.Combine(AppContext.BaseDirectory, name)}" Private="false" />"""));

    private static readonly string[] FlowReferences =
        ["QaaS.Playwright.dll", "Microsoft.Playwright.dll", "QaaS.Framework.SDK.dll", "Microsoft.Extensions.Configuration.Abstractions.dll"];

    private const string Props = """
        <Project>
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <MSBuildWarningsAsMessages>MSB3277</MSBuildWarningsAsMessages>
          </PropertyGroup>
        </Project>
        """;

    private static Dictionary<string, string> Files() => new()
    {
        // A custom output path: each project builds to repo/out/<project>/, not to its bin folder.
        ["repo/Directory.Build.props"] = Props.Replace("<PropertyGroup>",
            "<PropertyGroup>\n    <BaseOutputPath>$(MSBuildThisFileDirectory)out/$(MSBuildProjectName)/</BaseOutputPath>"),
        ["repo/Repo.slnx"] = """
            <Solution>
              <Project Path="src/Acme.Shared/Acme.Shared.csproj" />
              <Project Path="tests/Acme.Tests/Acme.Tests.csproj" />
              <Project Path="tools/Acme.Tools/Acme.Tools.csproj" />
            </Solution>
            """,
        ["repo/src/Acme.Shared/Acme.Shared.csproj"] = Project(References([.. FlowReferences])),
        ["repo/src/Acme.Shared/AcmeSignInFlow.cs"] = """
            using Microsoft.Playwright;

            namespace Acme.Shared;

            public sealed record SignInValues
            {
                public string User { get; init; } = "";

                public string Password { get; init; } = "";
            }

            public sealed class AcmeSignInFlow : QaaS.Playwright.BasePlaywrightFlow<SignInValues>
            {
                public override Task RunAsync(IPage page) => page.GotoAsync($"{BaseUrl}/login");
            }

            public sealed class OpenAcmeHomeFlow : QaaS.Playwright.BasePlaywrightFlow<SignInValues>
            {
                public override Task RunAsync(IPage page) => page.GotoAsync(BaseUrl);
            }
            """,
        ["repo/tests/Acme.Tests/Acme.Tests.csproj"] = Project("""    <ProjectReference Include="../../src/Acme.Shared/Acme.Shared.csproj" />"""),
        ["repo/tests/Acme.Tests/Tests/Smoke.qaas.yaml"] = """
            Probes:
              - Name: Ui
                Probe: PlaywrightFlowProbe
                ProbeConfiguration:
                  BaseUrl: ${QAAS_RECORDER_ACME_URL ?? https://acme.test/app}
                  SetupFlows: [AcmeSignInFlow]
                  Flows: [OpenAcmeHomeFlow]
                  FlowConfiguration:
                    AcmeSignInFlow:
                      User: alice
            """,
        ["repo/tools/Acme.Tools/Acme.Tools.csproj"] = Project(""),

        // A local feed, and a package folder of its own, so nothing reaches the machine's NuGet folder.
        ["pkg/nuget.config"] = """
            <configuration>
              <config><add key="globalPackagesFolder" value="packages" /></config>
              <packageSources><clear /><add key="local" value="feed" /></packageSources>
            </configuration>
            """,
        ["pkg/Directory.Build.props"] = Props,
        ["pkg/Acme.Portal/Acme.Portal.csproj"] = Project(References([.. FlowReferences]),
            "<PackageId>Acme.Portal</PackageId><Version>1.0.0</Version><IsPackable>true</IsPackable>"),
        ["pkg/Acme.Portal/PortalLoginFlow.cs"] = """
            using Microsoft.Playwright;

            namespace Acme.Portal;

            public sealed record PortalValues
            {
                public string User { get; init; } = "";
            }

            public sealed class PortalLoginFlow : QaaS.Playwright.BasePlaywrightFlow<PortalValues>
            {
                public override Task RunAsync(IPage page) => page.GotoAsync($"{BaseUrl}/login");
            }
            """,
        ["pkg/Acme.PortalTests/Acme.PortalTests.csproj"] = Project("""    <PackageReference Include="Acme.Portal" Version="1.0.0" />"""),
        ["pkg/Acme.PortalTests/Tests/Portal.qaas.yaml"] = "SetupFlows: [PortalLoginFlow]\n",
        // QaaS.Playwright as an old version was: its own copy, with what this one no longer has.
        ["pkg/OldQaaS/QaaS.Playwright.csproj"] = Project(References("Microsoft.Playwright.dll", "QaaS.Framework.SDK.dll",
            "Microsoft.Extensions.Configuration.Abstractions.dll"), "<AssemblyName>QaaS.Playwright</AssemblyName><Version>1.15.1</Version>"),
        ["pkg/OldQaaS/Old.cs"] = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Playwright;
            using QaaS.Framework.SDK.ContextObjects;

            namespace QaaS.Playwright
            {
                public interface IPlaywrightFlow
                {
                    Context Context { get; set; }

                    string BaseUrl { get; set; }

                    List<ValidationResult>? LoadAndValidateConfiguration(IConfiguration configuration);

                    Task RunAsync(IPage page);
                }

                public abstract class BasePlaywrightFlow<TConfiguration> : IPlaywrightFlow where TConfiguration : new()
                {
                    public Context Context { get; set; } = null!;

                    public string BaseUrl { get; set; } = "";

                    public TConfiguration Configuration { get; set; } = new();

                    public List<ValidationResult>? LoadAndValidateConfiguration(IConfiguration configuration) => [];

                    public abstract Task RunAsync(IPage page);
                }
            }

            namespace QaaS.Playwright.Browser
            {
                public static class BrowserDefaults
                {
                    public static string RemoteUrl { get; set; } = "";
                }
            }
            """,
        ["pkg/Acme.Legacy/Acme.Legacy.csproj"] = Project(References("Microsoft.Playwright.dll", "QaaS.Framework.SDK.dll",
            "Microsoft.Extensions.Configuration.Abstractions.dll") + "\n" + """    <ProjectReference Include="../OldQaaS/QaaS.Playwright.csproj" />"""),
        ["pkg/Acme.Legacy/LegacyLoginFlow.cs"] = """
            using Microsoft.Playwright;
            using QaaS.Playwright.Browser;

            namespace Acme.Legacy;

            public sealed record LegacyValues
            {
                public string User { get; init; } = "";
            }

            public sealed class LegacyLoginFlow : QaaS.Playwright.BasePlaywrightFlow<LegacyValues>
            {
                public override async Task RunAsync(IPage page) =>
                    await page.GotoAsync(BrowserDefaults.RemoteUrl.Length > 0 ? BrowserDefaults.RemoteUrl : $"{BaseUrl}/login");
            }
            """,
        ["pkg/Acme.LegacyTests/Acme.LegacyTests.csproj"] = Project("""    <ProjectReference Include="../Acme.Legacy/Acme.Legacy.csproj" />"""),
        ["pkg/Acme.LegacyTests/Tests/Legacy.qaas.yaml"] = "SetupFlows: [LegacyLoginFlow]\n",
    };

    /// <summary>The folder with <c>repo</c> and <c>pkg</c>, built; once, then from the temporary folder while it is the same.</summary>
    public static async Task<string> BuildAsync()
    {
        var files = Files();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", files.Select(file => file.Key + file.Value))
            + typeof(IPlaywrightFlow).Assembly.GetName().Version + AppContext.BaseDirectory)))[..12];
        var root = Path.Combine(Path.GetTempPath(), "qaas-recorder-login-fixtures", key);
        var done = Path.Combine(root, ".built");
        await Gate.WaitAsync();
        try
        {
            if (File.Exists(done)) return root;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            foreach (var (path, text) in files)
            {
                var full = Path.Combine(root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                await File.WriteAllTextAsync(full, text);
            }

            await DotnetAsync(root, "repo/tests/Acme.Tests", "build");
            await DotnetAsync(root, "pkg/Acme.Portal", "pack", "-o", Path.Combine(root, "pkg", "feed"));
            await DotnetAsync(root, "pkg/Acme.PortalTests", "build");
            await DotnetAsync(root, "pkg/Acme.LegacyTests", "build");
            await File.WriteAllTextAsync(done, "");
            return root;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static string Project(string items, string properties = "") => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>{properties}</PropertyGroup>
          <ItemGroup>
        {items}
          </ItemGroup>
        </Project>
        """;

    private static async Task DotnetAsync(string root, string project, params string[] command)
    {
        var folder = Path.Combine(root, project);
        var (exitCode, log) = await FlowBuild.DotnetAsync(folder, [command[0], "--nologo", "-v", "q", .. command[1..]], CancellationToken.None);
        Assert.That(exitCode, Is.Zero, $"dotnet {string.Join(' ', command)} {project}:\n{log}");
    }
}
