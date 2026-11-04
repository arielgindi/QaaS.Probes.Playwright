using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace QaaS.Playwright.Recorder.Generation;

/// <summary>
/// Writing C#, the one way generated code is written: string literals, doc text, regex patterns and identifiers, each
/// escaped or checked by one rule here, never by hand where it is used.
/// </summary>
internal static partial class CSharp
{
    /// <summary>The members every record has, which none of its properties may be named.</summary>
    public static readonly string[] RecordMembers =
        ["Equals", "GetHashCode", "ToString", "GetType", "EqualityContract", "Deconstruct", "PrintMembers", "Finalize"];

    /// <summary>
    /// Names a flow's code uses or has, which a flow, a setting or a step of that name would hide or clash with: the
    /// types its code names, its own members and those of every flow, and the members every record has.
    /// </summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(
    [
        "Task", "IPage", "ILocator", "IDialog", "IBrowserContext", "BasePlaywrightFlow", "Expect", "Assertions", "AriaRole",
        "Regex", "CultureInfo", "SelectOptionValue", "DateTime", "Uri", "NotImplementedException", "StringComparison",
        "Console", "Array", "RunAsync", "FileToUpload", "AcceptBrowserDialog", "CssText", "LoadAndValidateConfiguration",
        .. RecordMembers,
    ], StringComparer.Ordinal);

    private static readonly HashSet<string> Keywords =
    [
        .. """
           abstract as base bool break byte case catch char checked class const continue decimal default delegate do
           double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface
           internal is lock long namespace new null object operator out override params private protected public
           readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try
           typeof uint ulong unchecked unsafe ushort using virtual void volatile while page dialog
           """.Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries),
    ];

    /// <summary>
    /// A string literal. Characters that cannot be seen, such as bidi marks, are escaped so the reader knows they are
    /// there; letters of every script are kept as they are.
    /// </summary>
    public static string Literal(string text) => $"\"{Escape(text, interpolated: false)}\"";

    /// <summary>The text of a literal, escaped; in an interpolated string, braces are doubled.</summary>
    public static string Escape(string text, bool interpolated)
    {
        var literal = new StringBuilder(text.Length);
        foreach (var character in text)
            literal.Append(character switch
            {
                '\\' => @"\\",
                '"' => "\\\"",
                '\n' => @"\n",
                '\r' => @"\r",
                '\t' => @"\t",
                '{' when interpolated => "{{",
                '}' when interpolated => "}}",
                _ when IsInvisible(character) => $@"\u{(int)character:X4}",
                _ => character.ToString(),
            });
        return literal.ToString();
    }

    /// <summary>An identifier in PascalCase made of the ASCII letters and digits of the text, or null when it has none.</summary>
    public static string? Pascal(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var words = new List<string>();
        foreach (Match word in AsciiWord().Matches(text))
        {
            var value = word.Value;
            words.Add(value.All(char.IsUpper) && value.Length > 1
                ? char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant()
                : char.ToUpperInvariant(value[0]) + value[1..]);
        }

        if (words.Count == 0) return null;
        var identifier = string.Concat(words);
        return char.IsDigit(identifier[0]) ? "Field" + identifier : identifier;
    }

    /// <summary>
    /// An identifier in PascalCase from the text (<see cref="Pascal"/>), else <paramref name="fallback"/>: never null, as
    /// a text of no ASCII letter or digit (underscores, marks, another script) makes none.
    /// </summary>
    public static string Identifier(string? text, string fallback) => Pascal(text) ?? fallback;

    /// <summary>A text in an XML doc comment, on one line: a line break or another control character in it is a space.</summary>
    public static string Doc(string text) =>
        LineBreak().Replace(text, " ").Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    /// <summary>A regex pattern that matches the text as it is, in the syntax .NET and JavaScript share.</summary>
    public static string Pattern(string text) => RegexSpecial().Replace(text, @"\$0");

    /// <summary>The identifier in camelCase, made safe as a local variable name.</summary>
    public static string Camel(string pascal)
    {
        var camel = char.ToLowerInvariant(pascal[0]) + pascal[1..];
        return Keywords.Contains(camel) ? camel + "Field" : camel;
    }

    /// <summary>The text is ASCII letters, digits, spaces and simple punctuation, as an English label is.</summary>
    public static bool IsAsciiText(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.All(character => character < 128) && text.Any(char.IsLetter);

    public static string Bool(bool value) => value ? "true" : "false";

    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static bool IsInvisible(char character) =>
        char.IsControl(character) || character is '\x00A0' or '\x00AD' or '\x0085' or '\x061C'
            or (>= '\x200B' and <= '\x200F') or (>= '\x2028' and <= '\x202E')
            or (>= '\x2066' and <= '\x2069') or '\xFEFF';

    [GeneratedRegex("[A-Za-z0-9]+")]
    private static partial Regex AsciiWord();

    [GeneratedRegex(@"[\p{Cc}\u2028\u2029]")]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"[\\^$.|?*+()\[\]{}]")]
    private static partial Regex RegexSpecial();
}
