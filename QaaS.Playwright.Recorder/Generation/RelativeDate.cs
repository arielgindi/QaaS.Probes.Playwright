using System.Globalization;
using System.Text.RegularExpressions;

namespace QaaS.Playwright.Recorder.Generation;

/// <summary>
/// A date typed as text, as days from the day it was typed: <c>06/11/2026</c> typed on 3 November 2026 is 3 days from
/// then, in the format <c>dd/MM/yyyy</c>. The flow can then type the date as many days from the day it runs.
/// </summary>
internal static partial class RelativeDate
{
    // Further away, a date is a fixed one, such as a birthday.
    private const int MaxDays = 366;

    /// <summary>
    /// The format of <paramref name="text"/> and its days from <paramref name="day"/>, when it is a date of a
    /// four-digit year, day, month and year, or year, month and day, within a year of that day. A day and a month that
    /// both read as either are told apart only by <paramref name="hint"/>, what the field says of its format
    /// (<c>dd/mm/yyyy</c>): how close a date is to the day tells nothing of how the app reads it, and the flow would type
    /// it the other way round from the next day on.
    /// </summary>
    public static (string Format, int Days)? Of(string text, DateTime day, string? hint = null)
    {
        if (Parts().Match(text) is not { Success: true } match) return null;
        var (first, separator, second, third) =
            (match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value, match.Groups[4].Value);
        string[] formats = first.Length == 4 && third.Length <= 2
            ? [string.Join(separator, "yyyy", Pad(second, 'M'), Pad(third, 'd'))]
            : third.Length == 4 && first.Length <= 2
                ? [string.Join(separator, Pad(first, 'd'), Pad(second, 'M'), "yyyy"),
                    string.Join(separator, Pad(first, 'M'), Pad(second, 'd'), "yyyy")]
                : [];
        var read = formats.Select(format => DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? ((string Format, int Days)?)(format, (date.Date - day.Date).Days)
                : null)
            .OfType<(string Format, int Days)>().ToList();
        if (read.Count == 2 && read[0].Days != read[1].Days)
            read = Hinted(hint) switch
            {
                'd' => [read[0]],
                'M' => [read[1]],
                _ => [],
            };
        return read.Where(date => Math.Abs(date.Days) <= MaxDays).Select(date => ((string, int)?)date).FirstOrDefault();
    }

    /// <summary>The day an action was done, from its time in Unix milliseconds, on this machine's clock.</summary>
    public static DateTime DayOf(long unixMs) => DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime().Date;

    /// <summary>The setting of the days, named after the date's: <c>DueDate</c> gives <c>DueInDays</c>.</summary>
    public static string Name(string setting) =>
        setting.Length > 4 && setting.EndsWith("Date", StringComparison.Ordinal) ? setting[..^4] + "InDays" : setting + "InDays";

    /// <summary>The flow's property that types the date, named after the setting: <c>DueInDays</c> gives <c>DueDate</c>.</summary>
    public static string Member(string setting) =>
        (setting.Length > 6 && setting.EndsWith("InDays", StringComparison.Ordinal) ? setting[..^6] : setting) + "Date";

    // Which comes first a field's placeholder or label says, as "dd/mm/yyyy" or "MM-DD-YYYY": 'd', 'M', or none.
    private static char? Hinted(string? hint) =>
        hint is null ? null : DayFirst().IsMatch(hint) ? 'd' : MonthFirst().IsMatch(hint) ? 'M' : null;

    [GeneratedRegex(@"\bd{1,2}[./-]m{1,2}\b", RegexOptions.IgnoreCase)]
    private static partial Regex DayFirst();

    [GeneratedRegex(@"\bm{1,2}[./-]d{1,2}\b", RegexOptions.IgnoreCase)]
    private static partial Regex MonthFirst();

    // A part with a leading zero, or of two digits, is written with two: "03" gives dd, "3" d.
    private static string Pad(string part, char letter) => part.Length == 1 ? $"{letter}" : $"{letter}{letter}";

    [GeneratedRegex(@"^(\d{1,4})([./-])(\d{1,2})\2(\d{1,4})$")]
    private static partial Regex Parts();
}
