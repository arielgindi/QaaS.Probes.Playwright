namespace QaaS.Probes.Playwright.Recorder;

/// <summary>
/// CLI tool that wraps Playwright codegen and saves the output as a C# flow class
/// ready to use with PlaywrightFlowProbe.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args is [])
            return Usage();
        if (args is ["install"])
            return Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (args is ["record", var name, var url, ..])
            return Record(name, url, GetFlag(args, "--output-dir") ?? "Flows");
        return Usage(exitCode: 1);
    }

    private static int Record(string name, string url, string outDir)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"qaas-pw-{Guid.NewGuid():N}.cs");
        try
        {
            Console.WriteLine($"""

              Recording: {name}
              URL:       {url}

              Browser will open. Click around, close it when done.

            """);

            // Playwright codegen opens a browser with a recording toolbar.
            // Every click/type/navigate is captured and written as C# to the temp file.
            var exit = Microsoft.Playwright.Program.Main(
                ["codegen", "--target", "csharp-nunit", "--output", tmp, url]);

            if (exit != 0 || !File.Exists(tmp))
            {
                Console.Error.WriteLine("Recording cancelled.");
                return 1;
            }

            var actionLines = ExtractActions(File.ReadAllText(tmp));

            if (actionLines.Count == 0)
            {
                Console.Error.WriteLine("Nothing recorded — did you interact with the page?");
                return 1;
            }

            // Generate a flow class with the recorded actions
            Directory.CreateDirectory(outDir);
            var className = ToPascalCase(name);
            var configName = $"{className}Config";
            var outPath = Path.Combine(outDir, $"{className}.cs");
            var existed = File.Exists(outPath);

            var generated = $$"""
                using Microsoft.Playwright;
                using QaaS.Probes.Playwright;

                namespace Flows;

                /// <summary>
                /// Recorded browser flow. Edit Configuration properties to parameterize values.
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
                /// Add properties here, then reference them as Configuration.PropertyName in the flow.
                /// YAML under FlowConfiguration: binds to this record automatically.
                /// </summary>
                public record {{configName}} { }
                """;

            File.WriteAllText(outPath, generated);

            Console.WriteLine($"""

              {(existed ? "Updated" : "Created")}: {outPath}
              Class:     {className}
              Actions:   {actionLines.Count}

              Add to your test.qaas.yaml:
                Probe: PlaywrightFlowProbe
                ProbeConfiguration:
                  Flows: [{className}]

            """);
            return 0;
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    /// <summary>
    /// Extracts Playwright action lines from codegen output, stripping usings/class/test scaffolding.
    /// Normalizes Page. to page. so the flow uses the RunAsync parameter name.
    /// </summary>
    internal static List<string> ExtractActions(string csharpCode) =>
        csharpCode.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("await Page.") || l.StartsWith("await page."))
            .Select(l => l.Replace("await Page.", "await page."))
            .ToList();

    internal static string ToPascalCase(string name) =>
        string.Concat(name.Split('-', '_', ' ')
            .Select(w => char.ToUpper(w[0]) + w[1..]));

    private static string? GetFlag(string[] args, string flag)
    {
        var i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int Usage(int exitCode = 0)
    {
        Console.WriteLine("""
        QaaS Playwright Recorder

        Usage:
          record <name> <url> [--output-dir Flows]    Record a browser flow
          install                                     Install Chromium

        Examples:
          dotnet run -- install
          dotnet run -- record login https://my-app.com
          dotnet run -- record add-to-cart https://shop.com --output-dir ../Shared/Flows
        """);
        return exitCode;
    }
}
