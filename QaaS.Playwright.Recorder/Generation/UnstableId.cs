using System.Text.RegularExpressions;

namespace QaaS.Playwright.Recorder.Generation;

/// <summary>
/// Ids a framework generates, which change from one render or build to the next and so never make a locator: React's
/// <c>:r1:</c>, <c>«r1»</c> and <c>_r_1_</c>, MUI's <c>mui-123</c> and the ids it derives (<c>-option-3</c>,
/// <c>-listbox</c>), those numbered by the instance of a select or menu (<c>react-select-2-input</c>, antd's
/// <c>rc_select_0</c>, <c>downshift-1-menu</c>, Angular Material's <c>mat-option-4</c>), Headless UI's and Radix's,
/// Select2's results, random-looking ids, and ids with a long run of digits (a record's number). A form's path to a
/// field, such as Formik's <c>items[0].price</c> or <c>contacts.0.phone</c>, is stable when each name in it is.
/// </summary>
internal static partial class UnstableId
{
    public static bool IsUnstable(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return true;
        // As CSS escapes it, a digit first: "\36 d9a47e9-…" is "6d9a47e9-…".
        id = CssUnescape(id);
        return Generated().IsMatch(id) || Guid().IsMatch(id) || HexRun().Matches(id).Any(run => run.Value.Count(char.IsAsciiDigit) >= 3
                                                                                              && run.Value.Count(char.IsAsciiLetter) >= 2)
               || (FieldPath().IsMatch(id) ? PathNames(id).Any(IsGuidLike) : IsGuidLike(id));
    }

    /// <summary>A CSS identifier as the id it stands for: <c>\36 d9a</c> gives <c>6d9a</c>, <c>\:r1\:</c> gives <c>:r1:</c>.</summary>
    public static string CssUnescape(string css) =>
        CssEscape().Replace(css, escape => escape.Groups["hex"].Success
            ? char.ConvertFromUtf32(Math.Min(Convert.ToInt32(escape.Groups["hex"].Value, 16), 0x10FFFF))
            : escape.Groups["char"].Value);

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

    [GeneratedRegex(@":[rR][0-9a-zA-Z]*:|«[rR][0-9a-zA-Z]*»|_[rR]_[0-9a-zA-Z]+_|(^|[^a-zA-Z])mui-\d+|-option-\d+|-listbox|\d{5,}" +
                    @"|^react-select-\d+-|^rc_select_\d+|^downshift-\d+-|^(mat|cdk)-[a-z-]*\d+$|^headlessui-|^radix-|^select2-.+-result-" +
                    @"|^rc[-_][a-z]+[-_]\d+([-_]|$)|^(rc-menu-uuid|ant-[a-z]+-\d+)")]
    private static partial Regex Generated();

    // A GUID anywhere in it, as Kendo's widgets number their parts: "6d9a47e9-59a2-4181-b023-d9bf611ee9b4_k-dialog-content".
    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex Guid();

    // A run of hex of a hash or a random id, its digits and letters mixed: "9b2b62df".
    [GeneratedRegex(@"(?<![0-9a-zA-Z])[0-9a-f]{8,}(?![0-9a-zA-Z])")]
    private static partial Regex HexRun();

    [GeneratedRegex(@"\\(?:(?<hex>[0-9a-fA-F]{1,6}) ?|(?<char>[^0-9a-fA-F\r\n]))")]
    private static partial Regex CssEscape();
}
