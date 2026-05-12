namespace QaaS.Probes.Playwright.Recorder;

internal static class FlowCodeGenerator
{
    /// <summary>
    /// Pulls the recorded "await page.X()" lines out of Playwright codegen output.
    /// Drops the initial GotoAsync(url) — Playwright codegen always emits one for the
    /// starting URL, but the probe navigates to Configuration.BaseUrl before flows run,
    /// so the hardcoded recorded URL would override the YAML env config.
    /// </summary>
    public static List<string> ExtractActions(string csharpCode)
    {
        var lines = csharpCode.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("await Page.") || l.StartsWith("await page."))
            .Select(l => l.Replace("await Page.", "await page."))
            .ToList();

        if (lines.Count > 0 && lines[0].StartsWith("await page.GotoAsync"))
            lines.RemoveAt(0);

        return lines;
    }

    /// <summary>"add-to-cart" / "my_flow" / "  hi  " → "AddToCart" / "MyFlow" / "Hi".</summary>
    public static string ToPascalCase(string name)
    {
        var parts = name.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            throw new ArgumentException($"Flow name '{name}' contains no usable characters.", nameof(name));
        return string.Concat(parts.Select(w => char.ToUpper(w[0]) + w[1..]));
    }

    public static string Render(string className, IEnumerable<string> actionLines, string namespaceName)
    {
        var body = string.Join("\n", actionLines.Select(l => $"        {l}"));
        var configName = $"{className}Config";
        return $$"""
            using System.Text.RegularExpressions;
            using Microsoft.Playwright;
            using QaaS.Probes.Playwright;

            namespace {{namespaceName}};

            /// <summary>
            /// Recorded browser flow.
            /// To parameterize: add properties to <see cref="{{configName}}"/>,
            /// then replace hardcoded values with Configuration.PropertyName.
            /// </summary>
            public class {{className}} : BasePlaywrightFlow<{{configName}}>
            {
                public override async Task RunAsync(IPage page)
                {
            {{body}}
                }
            }

            /// <summary>Configuration for {{className}}. Pass values from FlowConfiguration:{{className}}:.</summary>
            public record {{configName}} { }
            """;
    }

    /// <summary>
    /// Derives a namespace from the output directory: scans upward for a .csproj,
    /// uses its RootNamespace (or file name) + the path from the project root.
    /// Falls back to "Flows" when no .csproj is found.
    /// </summary>
    public static string DeriveNamespace(string outputDir)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(outputDir));
        var ancestor = dir;
        while (ancestor != null)
        {
            var csproj = ancestor.GetFiles("*.csproj").FirstOrDefault();
            if (csproj is not null)
            {
                var root = ReadRootNamespace(csproj.FullName) ?? Path.GetFileNameWithoutExtension(csproj.Name);
                var rel = Path.GetRelativePath(ancestor.FullName, dir.FullName)
                    .Replace(Path.DirectorySeparatorChar, '.')
                    .Replace(Path.AltDirectorySeparatorChar, '.');
                return string.IsNullOrEmpty(rel) || rel == "." ? root : $"{root}.{rel}";
            }
            ancestor = ancestor.Parent;
        }
        return "Flows";
    }

    private static string? ReadRootNamespace(string csprojPath)
    {
        try
        {
            var doc = System.Xml.Linq.XDocument.Load(csprojPath);
            return doc.Descendants("RootNamespace").FirstOrDefault()?.Value?.Trim();
        }
        catch { return null; }
    }
}
