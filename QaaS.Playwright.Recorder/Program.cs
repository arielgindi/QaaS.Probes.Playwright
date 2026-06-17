namespace QaaS.Playwright.Recorder;

/// <summary>
/// CLI that wraps Playwright codegen and saves the output as a C# flow class
/// ready for PlaywrightFlowProbe. Uses the system-installed Google Chrome so
/// no extra browser download is needed. Two modes:
///   dotnet run                                 → interactive
///   dotnet run -- record &lt;name&gt; &lt;url&gt;          → quick record
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args is ["record", var name, var url, ..])
            return Record(name, url, GetFlag(args, "--output-dir") ?? "Flows");

        if (args is [] or ["record"])
            return Interactive();

        return Usage(exitCode: 1);
    }

    private static int Interactive()
    {
        ConsoleUi.Header();
        ConsoleUi.Info("Let's record a browser flow.\n");

        var url = ConsoleUi.Ask("What website do you want to record?", "");
        if (string.IsNullOrWhiteSpace(url))
        {
            ConsoleUi.Error("URL is required. Example: https://my-app.com/login");
            return 1;
        }

        var name = ConsoleUi.Ask("Give this flow a name", "my-flow");
        var outDir = ConsoleUi.Ask("Where to save it?", "Flows");

        Console.WriteLine();
        ConsoleUi.Separator();
        return Record(name, url, outDir);
    }

    private static int Record(string name, string url, string outDir)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"qaas-pw-{Guid.NewGuid():N}.codegen.txt");
        try
        {
            var className = FlowCodeGenerator.ToPascalCase(name);
            var outPath = Path.GetFullPath(Path.Combine(outDir, $"{className}.cs"));

            ConsoleUi.Info($"Flow:    {className}");
            ConsoleUi.Info($"URL:     {url}");
            ConsoleUi.Info($"Save to: {outPath}\n");

            ConsoleUi.Info(">>> Browser is opening — do your thing, then CLOSE the browser when done.\n");

            // Share auth state across recording sessions: first time, codegen starts
            // fresh (you log in once); every recording after, cookies/localStorage
            // load automatically and you're already signed in.
            var authPath = QaaS.Playwright.Engine.BrowserDefaults.AuthStatePath;
            Directory.CreateDirectory(Path.GetDirectoryName(authPath)!);

            var codegenArgs = new List<string>
            {
                "codegen",
                "--channel", QaaS.Playwright.Engine.BrowserDefaults.ChromeChannel,
                "--viewport-size", QaaS.Playwright.Engine.BrowserDefaults.RecorderViewport,
                "--target", "csharp-nunit",
                "--output", tmp,
                "--save-storage", authPath,
            };
            if (File.Exists(authPath))
                codegenArgs.AddRange(["--load-storage", authPath]);
            codegenArgs.Add(url);

            var exit = Microsoft.Playwright.Program.Main([.. codegenArgs]);

            if (exit != 0 || !File.Exists(tmp))
            {
                ConsoleUi.Error("Recording cancelled or browser closed before saving.");
                return 1;
            }

            var actions = FlowCodeGenerator.ExtractActions(File.ReadAllText(tmp));
            if (actions.Count == 0)
            {
                ConsoleUi.Error("No actions recorded. Interact with the page before closing.");
                return 1;
            }

            Directory.CreateDirectory(outDir);
            var existed = File.Exists(outPath);

            // Protect manual edits: never overwrite an existing flow file. Write to
            // `<Name>.recorded.cs` instead — user merges the new actions into the
            // existing file (which has their parameterization).
            var writePath = existed
                ? Path.Combine(outDir, $"{className}.recorded.cs")
                : outPath;

            var ns = FlowCodeGenerator.DeriveNamespace(outDir);
            File.WriteAllText(writePath, FlowCodeGenerator.Render(className, actions, ns));

            PrintNextSteps(className, url, writePath, existed, actions.Count);
            return 0;
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort */ }
        }
    }

    private static void PrintNextSteps(string className, string url, string outPath, bool existed, int actionCount)
    {
        ConsoleUi.Separator();
        ConsoleUi.Success($"SAVED {outPath}");
        ConsoleUi.Info($"{actionCount} actions recorded");
        if (existed)
            ConsoleUi.Info("(existing flow preserved — merge new actions into the original by hand)");
        Console.WriteLine();

        var baseUrl = TryGetAuthority(url);
        ConsoleUi.Info("Next steps:\n");
        ConsoleUi.Info("1. Add to your test.qaas.yaml:");
        ConsoleUi.Hint("Sessions:");
        ConsoleUi.Hint("  - Name: MySession");
        ConsoleUi.Hint("    Probes:");
        ConsoleUi.Hint($"      - Name: {className}");
        ConsoleUi.Hint($"        Probe: PlaywrightFlowProbe");
        ConsoleUi.Hint($"        ProbeConfiguration:");
        ConsoleUi.Hint($"          BaseUrl: {baseUrl}");
        ConsoleUi.Hint($"          Flows: [{className}]");
        Console.WriteLine();
        ConsoleUi.Info("2. Run it:");
        ConsoleUi.Hint("dotnet run -- run test.qaas.yaml");
        Console.WriteLine();
    }

    private static string TryGetAuthority(string url)
    {
        try { return new Uri(url).GetLeftPart(UriPartial.Authority); }
        catch { return url; }
    }

    private static string? GetFlag(string[] args, string flag)
    {
        var i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
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
