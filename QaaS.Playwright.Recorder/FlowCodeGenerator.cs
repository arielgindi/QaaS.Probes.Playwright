using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace QaaS.Playwright.Recorder;

/// <summary>
/// Turns Playwright codegen output into a compilable <c>BasePlaywrightFlow</c>. It keeps both the recorded
/// actions and the recorded <c>Expect(...)</c> assertions, rewrites the codegen fixture's <c>Page</c> to the
/// generated method's <c>page</c> parameter, and sanitizes names into valid C# identifiers.
/// </summary>
internal static partial class FlowCodeGenerator
{
    /// <summary>
    /// Pulls the recorded <c>await page.X()</c> actions and <c>await Expect(...)</c> assertions out of codegen
    /// output. A statement that codegen wraps across several lines (e.g. a long locator chain) is rejoined into a
    /// single statement, so the rendered flow always compiles. Drops only the initial <c>GotoAsync(startUrl)</c> —
    /// the probe navigates to <c>BaseUrl</c> itself.
    /// </summary>
    public static List<string> ExtractActions(string csharpCode)
    {
        var statements = new List<string>();
        string? pending = null;

        foreach (var line in csharpCode.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Outside a statement, only a recognized flow statement starts one; everything else (usings, braces,
            // the fixture scaffold) is skipped. Inside one, every line is a continuation until the statement ends.
            if (pending is null && !StartsFlowStatement(line))
                continue;

            pending = pending is null ? line : Join(pending, line);
            if (!pending.EndsWith(';'))
                continue;   // statement wrapped onto the next line — keep accumulating

            statements.Add(NormalizePageReference(pending));
            pending = null;
        }

        // The probe navigates to BaseUrl itself, so drop a leading GotoAsync(startUrl) the recorder captured.
        if (statements is [var first, ..] && first.StartsWith("await page.GotoAsync", StringComparison.Ordinal))
            statements.RemoveAt(0);

        return statements;
    }

    private static bool StartsFlowStatement(string line) =>
        line.StartsWith("await Page.", StringComparison.Ordinal)
        || line.StartsWith("await page.", StringComparison.Ordinal)
        || line.StartsWith("await Expect(", StringComparison.Ordinal);

    // Rejoin a statement codegen wrapped across lines: a fluent continuation (".ClickAsync()") binds tight to the
    // previous segment; anything else keeps one separating space, so the result reads — and compiles — as written.
    private static string Join(string statement, string continuation) =>
        continuation.StartsWith('.') ? statement + continuation : $"{statement} {continuation}";

    // Codegen's NUnit target drives the test fixture's `Page` property; the generated flow receives a `page`
    // parameter instead. Rewrite member access (`Page.`) and the page-level assertion (`Expect(Page)`).
    private static string NormalizePageReference(string line) =>
        PageMemberAccess().Replace(line, "page.").Replace("Expect(Page)", "Expect(page)");

    /// <summary>Turns a free-form name such as <c>add-to-cart</c> or <c>2fa_login</c> into a valid PascalCase type name.</summary>
    public static string ToPascalCase(string name)
    {
        var words = name
            .Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new string(word.Where(char.IsLetterOrDigit).ToArray()))
            .Where(word => word.Length > 0)
            .ToList();

        if (words.Count == 0)
            throw new ArgumentException($"Flow name '{name}' contains no usable characters.", nameof(name));

        var identifier = string.Concat(words.Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
        // A C# identifier cannot start with a digit (e.g. "2fa-login" -> "2faLogin").
        return char.IsDigit(identifier[0]) ? "Flow" + identifier : identifier;
    }

    public static string Render(string className, IEnumerable<string> actionLines, string namespaceName)
    {
        var body = string.Join("\n", actionLines.Select(line => $"        {line}"));
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

    /// <summary>
    /// Derives a namespace from the output directory: scans upward for a .csproj and combines its RootNamespace
    /// (or file name) with the path from the project root, sanitizing each segment into a valid identifier.
    /// Falls back to <c>Flows</c> when no .csproj is found.
    /// </summary>
    public static string DeriveNamespace(string outputDir)
    {
        var outputDirectory = new DirectoryInfo(Path.GetFullPath(outputDir));
        for (var ancestor = outputDirectory; ancestor is not null; ancestor = ancestor.Parent)
        {
            var projectFile = ancestor.GetFiles("*.csproj").FirstOrDefault();
            if (projectFile is null) continue;

            var root = ReadRootNamespace(projectFile.FullName) ?? Path.GetFileNameWithoutExtension(projectFile.Name);
            var relative = Path.GetRelativePath(ancestor.FullName, outputDirectory.FullName)
                .Replace(Path.DirectorySeparatorChar, '.')
                .Replace(Path.AltDirectorySeparatorChar, '.');

            return string.IsNullOrEmpty(relative) || relative == "."
                ? SanitizeNamespace(root)
                : $"{SanitizeNamespace(root)}.{SanitizeNamespace(relative)}";
        }

        return "Flows";
    }

    private static string SanitizeNamespace(string value) =>
        string.Join('.', value.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(SanitizeIdentifier));

    private static string SanitizeIdentifier(string segment)
    {
        var cleaned = new string(segment.Where(character => char.IsLetterOrDigit(character) || character == '_').ToArray());
        if (cleaned.Length == 0) return "Flows";
        return char.IsDigit(cleaned[0]) ? "_" + cleaned : cleaned;
    }

    private static string? ReadRootNamespace(string csprojPath)
    {
        try
        {
            return XDocument.Load(csprojPath).Descendants("RootNamespace").FirstOrDefault()?.Value?.Trim();
        }
        catch (Exception failure) when (failure is XmlException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"\bPage\.")]
    private static partial Regex PageMemberAccess();
}
