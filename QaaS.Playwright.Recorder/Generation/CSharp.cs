using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace QaaS.Playwright.Recorder.Generation;

/// <summary>Writing C#: string literals and identifiers.</summary>
internal static partial class CSharp
{
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
}

/// <summary>The invisible marks that order right-to-left and left-to-right text, which a typed value must not hold.</summary>
internal static partial class Bidi
{
    public static string Strip(string text) => Marks().Replace(text, "");

    // Escaped in the C# string, so the pattern itself holds the characters.
    [GeneratedRegex("[\x200B\x200E\x200F\x202A-\x202E\x2066-\x2069\x061C]")]
    private static partial Regex Marks();
}
