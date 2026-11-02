using System.Text.RegularExpressions;

namespace QaaS.Playwright.Recorder.Generation;

/// <summary>
/// Ids a framework generates, which change from one render or build to the next and so never make a locator: React's
/// <c>:r1:</c>, <c>«r1»</c> and <c>_r_1_</c>, MUI's <c>mui-123</c> and the ids it derives (<c>-option-3</c>,
/// <c>-listbox</c>), random-looking ids, and ids with a long run of digits (a record's number). A form's path to a
/// field, such as Formik's <c>items[0].price</c> or <c>contacts.0.phone</c>, is stable when each name in it is.
/// </summary>
internal static partial class UnstableId
{
    public static bool IsUnstable(string? id) =>
        string.IsNullOrWhiteSpace(id) || Generated().IsMatch(id)
        || (FieldPath().IsMatch(id) ? PathNames(id).Any(IsGuidLike) : IsGuidLike(id));

    // The names in a path, without the list indexes between them.
    private static IEnumerable<string> PathNames(string path) =>
        path.Split('.', '[', ']').Where(name => name.Length > 0 && !name.All(char.IsAsciiDigit));

    /// <summary>
    /// Playwright's own test (selectorGenerator.ts <c>isGuidLike</c>): the character class changes at least once
    /// every four characters. Upper to lower case is no change, so camelCase names are kept.
    /// </summary>
    public static bool IsGuidLike(string id)
    {
        string? last = null;
        var transitions = 0;
        foreach (var character in id)
        {
            if (character is '-' or '_') continue;
            var type = character switch
            {
                >= 'a' and <= 'z' => "lower",
                >= 'A' and <= 'Z' => "upper",
                >= '0' and <= '9' => "digit",
                _ => "other",
            };
            if (type == "lower" && last == "upper")
            {
                last = type;
                continue;
            }

            if (last is not null && last != type) transitions++;
            last = type;
        }

        return transitions >= id.Length / 4.0;
    }

    // Names joined by dots, each maybe with list indexes ([0]), or with indexes as names of their own (.0.).
    [GeneratedRegex(@"^[A-Za-z_$][\w$]*(\[\d{1,4}\])*(\.([A-Za-z_$][\w$]*|\d{1,4})(\[\d{1,4}\])*)+$|^[A-Za-z_$][\w$]*(\[\d{1,4}\])+$")]
    private static partial Regex FieldPath();

    [GeneratedRegex(@":[rR][0-9a-zA-Z]*:|«[rR][0-9a-zA-Z]*»|_[rR]_[0-9a-zA-Z]+_|(^|[^a-zA-Z])mui-\d+|-option-\d+|-listbox|\d{5,}")]
    private static partial Regex Generated();
}
