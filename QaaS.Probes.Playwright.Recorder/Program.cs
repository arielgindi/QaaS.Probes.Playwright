namespace QaaS.Probes.Playwright.Recorder;

/// <summary>
/// CLI tool that wraps Playwright's built-in codegen recorder and saves the output
/// as a C# flow class ready to use with PlaywrightFlowProbe.
///
/// Two modes:
/// - Interactive: just run "dotnet run" — asks URL, name, output folder
/// - Quick:       "dotnet run -- record login https://my-app.com"
///
/// The recorder does NOT build its own browser automation.
/// It delegates to Playwright's codegen (maintained by Microsoft) which generates
/// C# code for every user action. We extract the action lines and wrap them
/// in a BasePlaywrightFlow class.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args is ["install"])
            return Microsoft.Playwright.Program.Main(["install", "chromium"]);

        if (args is ["record", var name, var url, ..])
            return Record(name, url, GetFlag(args, "--output-dir") ?? "Flows");

        if (args is [] or ["record"])
            return Interactive();

        return Usage(exitCode: 1);
    }

    private static int Interactive()
    {
        Header();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  Let's record a browser flow.\n");
        Console.ResetColor();

        var url = Ask("  What website do you want to record?", "");
        if (string.IsNullOrWhiteSpace(url))
        {
            Error("URL is required. Example: https://my-app.com/login");
            return 1;
        }

        var name = Ask("  Give this flow a name", "my-flow");
        if (string.IsNullOrWhiteSpace(name))
        {
            Error("Flow name is required. Example: login, create-mission, checkout");
            return 1;
        }

        var outDir = Ask("  Where to save it?", "Flows");
        if (string.IsNullOrWhiteSpace(outDir)) outDir = "Flows";

        Console.WriteLine();
        Separator();

        return Record(name, url, outDir);
    }

    private static int Record(string name, string url, string outDir)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"qaas-pw-{Guid.NewGuid():N}.cs");
        try
        {
            var className = ToPascalCase(name);
            var outPath = Path.GetFullPath(Path.Combine(outDir, $"{className}.cs"));

            Info($"Flow:    {className}");
            Info($"URL:     {url}");
            Info($"Save to: {outPath}");
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  >>> Browser is opening...");
            Console.WriteLine("  >>> Do your thing, then CLOSE the browser when you're done.");
            Console.ResetColor();
            Console.WriteLine();

            // Playwright codegen opens a browser with a recording toolbar.
            // Every click, type, navigate is captured as C# code.
            // --target csharp-nunit gives us NUnit-style output (simpler to parse than raw csharp).
            // --output writes to a temp file so we can read it after the browser closes.
            var exit = Microsoft.Playwright.Program.Main(
                ["codegen", "--target", "csharp-nunit", "--output", tmp, url]);

            if (exit != 0 || !File.Exists(tmp))
            {
                Error("Recording cancelled or browser closed before saving.");
                return 1;
            }

            var actionLines = ExtractActions(File.ReadAllText(tmp));

            if (actionLines.Count == 0)
            {
                Error("No actions recorded. Make sure you interact with the page before closing.");
                return 1;
            }

            Directory.CreateDirectory(outDir);
            var configName = $"{className}Config";
            var existed = File.Exists(outPath);

            // Generate a complete C# class with:
            // - The recorded Playwright actions inside RunAsync
            // - An empty config record the user can add properties to later
            // - Comments explaining how to parameterize
            var generated = $$"""
                using Microsoft.Playwright;
                using QaaS.Probes.Playwright;

                namespace Flows;

                /// <summary>
                /// Recorded browser flow.
                /// To parameterize: add properties to <see cref="{{configName}}"/>,
                /// then replace hardcoded values with Configuration.PropertyName.
                /// </summary>
                public class {{className}} : BasePlaywrightFlow<{{configName}}>
                {
                    public override async Task RunAsync(IPage page)
                    {
                {{string.Join("\n", actionLines.Select(l => $"        {l}"))}}
                    }
                }

                /// <summary>
                /// Configuration for {{className}}.
                /// Add properties here and pass values from YAML under FlowConfiguration:{{className}}:
                /// </summary>
                public record {{configName}} { }
                """;

            File.WriteAllText(outPath, generated);

            Separator();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  {(existed ? "UPDATED" : "SAVED")} {outPath}");
            Console.ResetColor();
            Console.WriteLine($"  {actionLines.Count} actions recorded\n");

            string baseUrl;
            try { baseUrl = new Uri(url).GetLeftPart(UriPartial.Authority); }
            catch { baseUrl = url; }

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("  Next steps:\n");
            Console.ResetColor();

            Console.WriteLine("  1. Add to your test.qaas.yaml:\n");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"     Sessions:");
            Console.WriteLine($"       - Name: MySession");
            Console.WriteLine($"         Probes:");
            Console.WriteLine($"           - Name: {className}");
            Console.WriteLine($"             Probe: PlaywrightFlowProbe");
            Console.WriteLine($"             ProbeConfiguration:");
            Console.WriteLine($"               BaseUrl: {baseUrl}");
            Console.WriteLine($"               Flows: [{className}]");
            Console.ResetColor();
            Console.WriteLine();

            Console.WriteLine("  2. To parameterize values, edit the generated file:");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"     - Add properties to {configName}");
            Console.WriteLine($"     - Use Configuration.YourProperty in the flow");
            Console.WriteLine($"     - Pass values in YAML under FlowConfiguration:{className}:");
            Console.ResetColor();
            Console.WriteLine();

            Console.WriteLine("  3. Run it:");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("     dotnet run -- run test.qaas.yaml");
            Console.ResetColor();
            Console.WriteLine();

            return 0;
        }
        finally
        {
            // Always clean up the temp file — even if codegen failed
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    /// <summary>
    /// Extracts Playwright action lines from codegen output.
    /// Codegen produces a full NUnit test class with usings, attributes, class scaffolding.
    /// We only want the "await Page.Something()" lines — the actual recorded actions.
    /// Also normalizes "Page." to "page." to match the RunAsync(IPage page) parameter name.
    /// </summary>
    internal static List<string> ExtractActions(string csharpCode) =>
        csharpCode.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("await Page.") || l.StartsWith("await page."))
            .Select(l => l.Replace("await Page.", "await page."))
            .ToList();

    /// <summary>Converts "add-to-cart" or "my_flow" to "AddToCart" or "MyFlow".</summary>
    internal static string ToPascalCase(string name) =>
        string.Concat(name.Split('-', '_', ' ')
            .Select(w => char.ToUpper(w[0]) + w[1..]));

    private static void Header()
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("  ╔═══════════════════════════════════╗");
        Console.WriteLine("  ║   QaaS Playwright Recorder        ║");
        Console.WriteLine("  ╚═══════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void Separator()
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  ───────────────────────────────────────");
        Console.ResetColor();
    }

    private static void Info(string msg)
    {
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.Write("  ");
        Console.ResetColor();
        Console.WriteLine(msg);
    }

    private static void Error(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n  {msg}\n");
        Console.ResetColor();
    }

    private static string Ask(string prompt, string defaultValue)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"  {prompt}");
        Console.ResetColor();

        if (!string.IsNullOrEmpty(defaultValue))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($" [{defaultValue}]");
            Console.ResetColor();
        }

        Console.Write(": ");
        Console.ForegroundColor = ConsoleColor.Green;
        var input = Console.ReadLine()?.Trim() ?? "";
        Console.ResetColor();

        return string.IsNullOrEmpty(input) ? defaultValue : input;
    }

    private static string? GetFlag(string[] args, string flag)
    {
        var i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int Usage(int exitCode = 0)
    {
        Header();
        Console.WriteLine("  Usage:");
        Console.WriteLine("    dotnet run                                          Interactive mode");
        Console.WriteLine("    dotnet run -- record <name> <url>                   Quick record");
        Console.WriteLine("    dotnet run -- record <name> <url> --output-dir Dir  Record to folder");
        Console.WriteLine("    dotnet run -- install                               Install Chromium");
        Console.WriteLine();
        Console.WriteLine("  Examples:");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("    dotnet run");
        Console.WriteLine("    dotnet run -- record login https://my-app.com");
        Console.ResetColor();
        Console.WriteLine();
        return exitCode;
    }
}
