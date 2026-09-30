using System.Text.RegularExpressions;

namespace QaaS.Playwright.Recorder;

/// <summary>
/// Turns Playwright codegen output into a compilable flow class: the recorded actions and <c>Expect(...)</c>
/// assertions, with codegen's <c>Page</c> fixture property rewritten to the flow's <c>page</c> parameter.
/// </summary>
internal static partial class FlowCodeGenerator
{
    /// <summary>
    /// The recorded statements, one entry each even when codegen wrapped one over several lines, without the first
    /// GotoAsync: the probe opens BaseUrl itself.
    /// </summary>
    public static List<string> ExtractActions(string codegenOutput)
    {
        var statements = new List<string>();
        string? pending = null;

        var lines = codegenOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            // Usings, braces and the fixture scaffold are skipped; inside a statement every line continues it.
            if (pending is null && !StartsFlowStatement(line)) continue;

            pending = pending is null ? line : Join(pending, line);
            if (!pending.EndsWith(';')) continue;

            statements.Add(PageMemberAccess().Replace(pending, "page.").Replace("Expect(Page)", "Expect(page)"));
            pending = null;
        }

        if (statements is [var first, ..] && first.StartsWith("await page.GotoAsync", StringComparison.Ordinal))
            statements.RemoveAt(0);
        return statements;
    }

    /// <summary>Turns a name such as <c>add-to-cart</c> into a type name (<c>AddToCart</c>).</summary>
    /// <exception cref="ArgumentException">The name has no letter or digit.</exception>
    public static string ToPascalCase(string name)
    {
        var words = name.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => string.Concat(word.Where(char.IsLetterOrDigit)))
            .Where(word => word.Length > 0)
            .ToList();
        if (words.Count == 0) throw new ArgumentException($"Flow name '{name}' has no letters or digits.", nameof(name));

        var identifier = string.Concat(words.Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
        // A type name cannot start with a digit: "2fa-login" becomes "Flow2faLogin".
        return char.IsDigit(identifier[0]) ? "Flow" + identifier : identifier;
    }

    public static string Render(string className, IEnumerable<string> actions, string namespaceName)
    {
        var body = string.Join("\n", actions.Select(action => $"        {action}"));
        var configName = $"{className}Config";
        return $$"""
            using System.Text.RegularExpressions;
            using Microsoft.Playwright;
            using QaaS.Playwright;
            using static Microsoft.Playwright.Assertions;

            namespace {{namespaceName}};

            /// <summary>
            /// Recorded browser flow. To parameterize: add properties to <see cref="{{configName}}"/>,
            /// then replace hardcoded values with Configuration.PropertyName.
            /// </summary>
            public sealed class {{className}} : BasePlaywrightFlow<{{configName}}>
            {
                public override async Task RunAsync(IPage page)
                {
            {{body}}
                }
            }

            /// <summary>Configuration for {{className}}. Pass values from FlowConfiguration:{{className}}:.</summary>
            public sealed record {{configName}};
            """;
    }

    private static bool StartsFlowStatement(string line) =>
        line.StartsWith("await Page.", StringComparison.Ordinal)
        || line.StartsWith("await page.", StringComparison.Ordinal)
        || line.StartsWith("await Expect(", StringComparison.Ordinal);

    // A fluent continuation (".ClickAsync()") joins tight; anything else keeps one space, so it compiles as written.
    private static string Join(string statement, string continuation) =>
        continuation.StartsWith('.') ? statement + continuation : $"{statement} {continuation}";

    [GeneratedRegex(@"\bPage\.")]
    private static partial Regex PageMemberAccess();
}
