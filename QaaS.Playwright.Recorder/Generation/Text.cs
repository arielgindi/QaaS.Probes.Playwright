using System.Text.RegularExpressions;

namespace QaaS.Playwright.Recorder.Generation;

/// <summary>
/// Text as the recorder tells it apart, by one rule each. Data, what a page holds or a person typed, is kept exactly:
/// the flow types and chooses it as it was. A name a person reads is compared without its bidi and zero-width marks, as
/// the capture script cleans the names it reports. A value is found in a text only where it is a word of its own, by
/// the planner and the writer alike.
/// </summary>
internal static partial class Text
{
    /// <summary>The bidi and zero-width marks a name a person reads is compared without, as the capture script strips them.</summary>
    public const string MarkClass = "\u200B\u200E\u200F\u202A-\u202E\u2066-\u2069\u061C";

    public static string WithoutMarks(string text) => Marks().Replace(text, "");

    /// <summary>
    /// What a field was filled with, exactly: the field's own value with its marks (<paramref name="raw"/>) when it says
    /// what the cleaned one does but for them, else the value the field settled on, re-formatted by the app.
    /// </summary>
    public static string Typed(string? value, string? raw) =>
        value is not null && raw is not null && raw != value && WithoutMarks(raw) == value ? raw : value ?? "";

    /// <summary>Where <paramref name="value"/> is in <paramref name="text"/> as a word of its own: "TradeCode 9" is in no "TradeCode 99".</summary>
    public static IEnumerable<int> WordsAt(string text, string value)
    {
        if (value.Length == 0) yield break;
        var from = 0;
        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + 1, StringComparison.Ordinal))
        {
            var end = at + value.Length;
            var whole = (at == 0 || !char.IsLetterOrDigit(text[at - 1]) || !char.IsLetterOrDigit(value[0]))
                        && (end == text.Length || !char.IsLetterOrDigit(text[end]) || !char.IsLetterOrDigit(value[^1]));
            if (!whole || at < from) continue;
            yield return at;
            from = end;
        }
    }

    /// <summary>The text with each place <paramref name="value"/> is a word of its own in it replaced.</summary>
    public static string ReplaceWords(string text, string value, string replacement)
    {
        var places = WordsAt(text, value).ToList();
        for (var index = places.Count - 1; index >= 0; index--)
            text = text[..places[index]] + replacement + text[(places[index] + value.Length)..];
        return text;
    }

    // Escaped in the C# string, so the pattern itself holds the characters.
    [GeneratedRegex("[" + MarkClass + "]")]
    private static partial Regex Marks();
}
