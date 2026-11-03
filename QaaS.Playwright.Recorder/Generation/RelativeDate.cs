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
    /// four-digit year, day, month and year, or year, month and day, within a year of that day. Of a day and a month
    /// that both read as either, the closer date.
    /// </summary>
    public static (string Format, int Days)? Of(string text, DateTime day)
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
        (string Format, int Days)? closest = null;
        foreach (var format in formats)
        {
            if (!DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            var days = (date.Date - day.Date).Days;
            if (Math.Abs(days) <= MaxDays && (closest is null || Math.Abs(days) < Math.Abs(closest.Value.Days)))
                closest = (format, days);
        }

        return closest;
    }

    /// <summary>The day an action was done, from its time in Unix milliseconds, on this machine's clock.</summary>
    public static DateTime DayOf(long unixMs) => DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime().Date;

    /// <summary>The setting of the days, named after the date's: <c>DueDate</c> gives <c>DueInDays</c>.</summary>
    public static string Name(string setting) =>
        setting.Length > 4 && setting.EndsWith("Date", StringComparison.Ordinal) ? setting[..^4] + "InDays" : setting + "InDays";

    /// <summary>The flow's property that types the date, named after the setting: <c>DueInDays</c> gives <c>DueDate</c>.</summary>
    public static string Member(string setting) =>
        (setting.Length > 6 && setting.EndsWith("InDays", StringComparison.Ordinal) ? setting[..^6] : setting) + "Date";

    // A part with a leading zero, or of two digits, is written with two: "03" gives dd, "3" d.
    private static string Pad(string part, char letter) => part.Length == 1 ? $"{letter}" : $"{letter}{letter}";

    [GeneratedRegex(@"^(\d{1,4})([./-])(\d{1,2})\2(\d{1,4})$")]
    private static partial Regex Parts();
}
