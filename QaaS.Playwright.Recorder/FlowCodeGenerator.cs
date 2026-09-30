using System.Text.RegularExpressions;

namespace QaaS.Playwright.Recorder;

/// <summary>
/// Turns Playwright codegen output into a compilable flow class: the recorded actions, <c>Expect(...)</c> assertions
/// and pop-ups, with codegen's <c>Page</c>, <c>Page1</c>, ... fixture properties rewritten to the flow's <c>page</c>
/// parameter and the <c>page1</c>, ... variables that hold the pop-ups.
/// </summary>
internal static partial class FlowCodeGenerator
{
    private const string StringLiteral = """
        "(?:\\.|[^"\\])*"
        """;

    /// <summary>
    /// The recorded statements, one entry each even when codegen wrapped one over several lines, as a pop-up's wait
    /// with its lambda, without the first GotoAsync: the probe opens BaseUrl itself.
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
            if (!pending.EndsWith(';') || !BracketsAreClosed(pending)) continue;

            statements.Add(PageOrStringLiteral().Replace(pending, match =>
                match.Groups["literal"].Success ? match.Value : $"page{match.Groups["popup"].Value}"));
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

    // An action or assertion, or a pop-up's wait: "var page1 = await Page.RunAndWaitForPopupAsync(async () =>".
    private static bool StartsFlowStatement(string line) =>
        line.StartsWith("await ", StringComparison.Ordinal) || line.StartsWith("var ", StringComparison.Ordinal);

    // A fluent continuation (".ClickAsync()") joins tight; anything else keeps one space, so it compiles as written.
    private static string Join(string statement, string continuation) =>
        continuation.StartsWith('.') ? statement + continuation : $"{statement} {continuation}";

    // A statement with a lambda, such as a pop-up's wait, goes on after the ';' that ends the lambda's first statement.
    private static bool BracketsAreClosed(string statement)
    {
        var code = StringLiterals().Replace(statement, "");
        return code.Count(character => character is '(' or '{' or '[')
               == code.Count(character => character is ')' or '}' or ']');
    }

    [GeneratedRegex(StringLiteral)]
    private static partial Regex StringLiterals();

    // Page or Page1 outside a string literal: a typed value or a label may well read "Page.Title".
    [GeneratedRegex($"""(?<literal>{StringLiteral})|\bPage(?<popup>\d*)\b""")]
    private static partial Regex PageOrStringLiteral();
}
