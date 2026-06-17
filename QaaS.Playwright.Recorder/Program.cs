using QaaS.Playwright.Engine;

namespace QaaS.Playwright.Recorder;

/// <summary>
/// CLI that wraps Playwright codegen and saves the output as a C# flow class ready for PlaywrightFlowProbe. It
/// uses the system-installed Google Chrome, so no extra browser download is needed. Two modes:
/// <list type="bullet">
///   <item><c>dotnet run</c> — interactive.</item>
///   <item><c>dotnet run -- record &lt;name&gt; &lt;url&gt; [--output-dir Dir]</c> — quick record.</item>
/// </list>
/// </summary>
public static class Program
{
    private const string DefaultOutputDir = "Flows";

    public static int Main(string[] args)
    {
        if (args is [] or ["record"])
            return Interactive();

        if (args is ["record", .. var recordArgs])
            return RecordFromArgs(recordArgs);

        return Usage(exitCode: 1);
    }

    private static int Interactive()
    {
        ConsoleUi.Header();
        ConsoleUi.Info("Let's record a browser flow.\n");

        var url = ConsoleUi.Ask("What website do you want to record?", "");
        if (!IsValidHttpUrl(url))
        {
            ConsoleUi.Error("A valid http(s) URL is required. Example: https://my-app.com/login");
            return 1;
        }

        var name = ConsoleUi.Ask("Give this flow a name", "my-flow");
        var outputDir = ConsoleUi.Ask("Where to save it?", DefaultOutputDir);

        Console.WriteLine();
        ConsoleUi.Separator();
        return Record(name, url, outputDir);
    }

    /// <summary>Parses <c>&lt;name&gt; &lt;url&gt; [--output-dir Dir]</c> in any order, validating as it goes.</summary>
    private static int RecordFromArgs(string[] args)
    {
        var outputDir = DefaultOutputDir;
        var positionals = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg == "--output-dir")
            {
                if (index + 1 >= args.Length || args[index + 1].StartsWith('-'))
                {
                    ConsoleUi.Error("--output-dir needs a directory value.");
                    return Usage(1);
                }
                outputDir = args[++index];
            }
            else if (arg.StartsWith('-'))
            {
                ConsoleUi.Error($"Unknown option '{arg}'.");
                return Usage(1);
            }
            else
            {
                positionals.Add(arg);
            }
        }

        if (positionals is not [var name, var url])
        {
            ConsoleUi.Error("Expected exactly: record <name> <url> [--output-dir Dir]");
            return Usage(1);
        }

        if (!IsValidHttpUrl(url))
        {
            ConsoleUi.Error($"'{url}' is not a valid http(s) URL. Example: https://my-app.com/login");
            return Usage(1);
        }

        return Record(name, url, outputDir);
    }

    private static int Record(string name, string url, string outputDir)
    {
        var codegenOutput = Path.Combine(Path.GetTempPath(), $"qaas-pw-{Guid.NewGuid():N}.codegen.txt");
        try
        {
            var className = FlowCodeGenerator.ToPascalCase(name);
            var outputPath = Path.GetFullPath(Path.Combine(outputDir, $"{className}.cs"));

            ConsoleUi.Info($"Flow:    {className}");
            ConsoleUi.Info($"URL:     {url}");
            ConsoleUi.Info($"Save to: {outputPath}\n");
            ConsoleUi.Info(">>> Browser is opening — do your thing, then CLOSE the browser when done.\n");

            if (RunCodegen(url, codegenOutput) is { } failure)
                return failure;

            var actions = FlowCodeGenerator.ExtractActions(File.ReadAllText(codegenOutput));
            if (actions.Count == 0)
            {
                ConsoleUi.Error("No actions recorded. Interact with the page before closing.");
                return 1;
            }

            return SaveFlow(className, url, actions, outputDir, outputPath);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            ConsoleUi.Error($"Could not save the recorded flow: {failure.Message}");
            return 1;
        }
        finally
        {
            TryDelete(codegenOutput);
        }
    }

    /// <summary>Runs Playwright codegen; returns an exit code on failure, or null on success.</summary>
    private static int? RunCodegen(string url, string codegenOutput)
    {
        // Share auth state across recording sessions: the first run starts fresh (log in once); later runs load
        // the saved cookies/localStorage so you are already signed in.
        var authStatePath = BrowserDefaults.AuthStatePath;
        Directory.CreateDirectory(Path.GetDirectoryName(authStatePath)!);

        var codegenArgs = new List<string>
        {
            "codegen",
            "--channel", BrowserDefaults.ChromeChannel,
            "--viewport-size", BrowserDefaults.RecorderViewport,
            "--target", "csharp-nunit",
            "--output", codegenOutput,
            "--save-storage", authStatePath,
        };
        if (File.Exists(authStatePath))
            codegenArgs.AddRange(["--load-storage", authStatePath]);
        codegenArgs.Add(url);

        var exitCode = Microsoft.Playwright.Program.Main([.. codegenArgs]);
        if (exitCode != 0 || !File.Exists(codegenOutput))
        {
            ConsoleUi.Error("Recording cancelled or browser closed before saving.");
            return 1;
        }

        return null;
    }

    private static int SaveFlow(string className, string url, List<string> actions, string outputDir, string outputPath)
    {
        Directory.CreateDirectory(outputDir);
        var alreadyExisted = File.Exists(outputPath);

        // Protect manual edits: never overwrite an existing flow file. Write to `<Name>.recorded.cs` instead so the
        // user can merge the new actions into the file they have already parameterized.
        var writePath = alreadyExisted ? Path.Combine(outputDir, $"{className}.recorded.cs") : outputPath;

        var flowNamespace = FlowCodeGenerator.DeriveNamespace(outputDir);
        File.WriteAllText(writePath, FlowCodeGenerator.Render(className, actions, flowNamespace));

        PrintNextSteps(className, url, writePath, alreadyExisted, actions.Count);
        return 0;
    }

    private static void PrintNextSteps(string className, string url, string outputPath, bool alreadyExisted, int actionCount)
    {
        ConsoleUi.Separator();
        ConsoleUi.Success($"SAVED {outputPath}");
        ConsoleUi.Info($"{actionCount} actions recorded");
        if (alreadyExisted)
            ConsoleUi.Info("(existing flow preserved — merge new actions into the original by hand)");
        Console.WriteLine();

        var baseUrl = TryGetAuthority(url);
        ConsoleUi.Info("Next steps:\n");
        ConsoleUi.Info("1. Add to your test.qaas.yaml:");
        ConsoleUi.Hint("Sessions:");
        ConsoleUi.Hint("  - Name: MySession");
        ConsoleUi.Hint("    Probes:");
        ConsoleUi.Hint($"      - Name: {className}");
        ConsoleUi.Hint("        Probe: PlaywrightFlowProbe");
        ConsoleUi.Hint("        ProbeConfiguration:");
        ConsoleUi.Hint($"          BaseUrl: {baseUrl}");
        ConsoleUi.Hint($"          Flows: [{className}]");
        Console.WriteLine();
        ConsoleUi.Info("2. Run it:");
        ConsoleUi.Hint("dotnet run -- run test.qaas.yaml");
        Console.WriteLine();
    }

    private static bool IsValidHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string TryGetAuthority(string url)
    {
        try
        {
            return new Uri(url).GetLeftPart(UriPartial.Authority);
        }
        catch (UriFormatException)
        {
            return url;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup of a temp file; ignore.
        }
    }

    private static int Usage(int exitCode = 0)
    {
        ConsoleUi.Header();
        Console.WriteLine("  Usage:");
        Console.WriteLine("    dotnet run                                          Interactive mode");
        Console.WriteLine("    dotnet run -- record <name> <url>                   Quick record");
        Console.WriteLine("    dotnet run -- record <name> <url> --output-dir Dir  Record to folder");
        Console.WriteLine();
        Console.WriteLine("  Uses your system-installed Google Chrome.");
        Console.WriteLine();
        return exitCode;
    }
}
