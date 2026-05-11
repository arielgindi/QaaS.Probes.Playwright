namespace QaaS.Probes.Playwright.Recorder;

internal static class FlowCodeGenerator
{
    /// <summary>Pulls the recorded "await page.X()" lines out of Playwright codegen output.</summary>
    public static List<string> ExtractActions(string csharpCode) =>
        csharpCode.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("await Page.") || l.StartsWith("await page."))
            .Select(l => l.Replace("await Page.", "await page."))
            .ToList();

    /// <summary>"add-to-cart" / "my_flow" / "  hi  " → "AddToCart" / "MyFlow" / "Hi".</summary>
    public static string ToPascalCase(string name)
    {
        var parts = name.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            throw new ArgumentException($"Flow name '{name}' contains no usable characters.", nameof(name));
        return string.Concat(parts.Select(w => char.ToUpper(w[0]) + w[1..]));
    }

    public static string Render(string className, IEnumerable<string> actionLines)
    {
        var body = string.Join("\n", actionLines.Select(l => $"        {l}"));
        var configName = $"{className}Config";
        return $$"""
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
            {{body}}
                }
            }

            /// <summary>Configuration for {{className}}. Pass values from FlowConfiguration:{{className}}:.</summary>
            public record {{configName}} { }
            """;
    }
}
