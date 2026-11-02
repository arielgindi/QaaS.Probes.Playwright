using System.Text.RegularExpressions;
using Microsoft.Playwright;
using QaaS.Playwright.Recorder.Capture;

namespace QaaS.Playwright.Recorder.Generation;

/// <summary>
/// Picks the locator of an element from the ones the capture script checked, and writes it as C#. In order, the first
/// that found the element alone wins: its Formik path (<c>[name='x']</c>), its own stable id, a MUI Select's field by
/// the path of its hidden input, the test id, its role and exact name, its row, and last Playwright's own generator.
/// </summary>
internal static partial class LocatorPolicy
{
    private static readonly string[] Order = ["name", "id", "select", "testid", "role", "roleInGroup", "row", "generator"];

    // A choice among radios is written with the option's name, so the name can come from the flow's settings.
    private static readonly string[] ByName = ["role", "roleInGroup", "nameValue"];

    /// <summary>
    /// The locator to write for the element; <paramref name="byName"/> prefers one that finds it by its name (a
    /// radio of a group). Null when nothing was captured.
    /// </summary>
    public static CapturedCandidate? Choose(CapturedTarget? target, bool byName = false)
    {
        if (target is null) return null;
        var usable = target.Candidates.Where(candidate => candidate.Matches && IsUsable(candidate)).ToList();
        foreach (var kind in byName ? [.. ByName, .. Order] : Order)
            if (usable.FirstOrDefault(candidate => candidate.Kind == kind) is { } found)
                return found;

        // Nothing found the element alone: the closest miss, which the flow's reader must fix.
        return target.Candidates.Where(IsUsable).OrderBy(candidate => candidate.Count <= 0 ? int.MaxValue : candidate.Count)
            .FirstOrDefault();
    }

    /// <summary>Why the locator needs a person's look, or null when it is fine.</summary>
    public static string? Problem(CapturedCandidate? candidate)
    {
        if (candidate is null) return "no locator was captured for this element";
        if (!candidate.Matches)
            return candidate.Count > 1
                ? $"this locator found {candidate.Count} elements when recorded"
                : "this locator did not find the element when recorded";
        if (candidate.Kind != "generator") return null;
        var code = candidate.Csharp ?? "";
        if (Position().IsMatch(code)) return "Playwright's locator picks the element by its position";
        if (CssStrings(code).Any(HasClass)) return "Playwright's locator picks the element by a style class";
        if (CssStrings(code).SelectMany(IdsIn).Any(UnstableId.IsUnstable)) return "Playwright's locator uses a generated id";
        return null;
    }

    /// <summary>
    /// The locator as C#, from <paramref name="root"/> (<c>page</c>, <c>dialog</c>, ...). A part's name, text or CSS can
    /// be replaced by an expression, e.g. <c>Configuration.VisitType</c>.
    /// </summary>
    public static string Render(string root, IEnumerable<LocatorPart> parts, Func<LocatorPart, string?>? nameOf = null) =>
        root + string.Concat(parts.Select(part => "." + RenderPart(part, nameOf?.Invoke(part))));

    public static string RenderGenerated(string root, CapturedCandidate generated) => $"{root}.{generated.Csharp}";

    /// <summary>The role's member of <see cref="AriaRole"/>, e.g. <c>Menuitem</c>; null for a role it does not have.</summary>
    public static string? AriaRoleName(string? role) =>
        role is not null && Enum.TryParse<AriaRole>(role, ignoreCase: true, out var value) ? value.ToString() : null;

    private static string RenderPart(LocatorPart part, string? name) => part.M switch
    {
        "css" => $"Locator({name ?? CSharp.Literal(part.Css ?? "")})",
        "testid" => $"GetByTestId({CSharp.Literal(part.Value ?? "")})",
        "filter" => $"Filter(new() {{ HasText = {name ?? CSharp.Literal(part.HasText ?? "")} }})",
        "role" => RenderRole(part, name),
        _ => throw new InvalidOperationException($"Unknown locator part '{part.M}'."),
    };

    private static string RenderRole(LocatorPart part, string? name)
    {
        var role = $"AriaRole.{AriaRoleName(part.Role)}";
        if (part.Regex is not null) return $"GetByRole({role}, new() {{ NameRegex = new Regex({CSharp.Literal(part.Regex)}) }})";
        if (part.Name is null) return $"GetByRole({role})";
        var exact = part.Exact ? ", Exact = true" : "";
        return $"GetByRole({role}, new() {{ Name = {name ?? CSharp.Literal(part.Name)}{exact} }})";
    }

    private static bool IsUsable(CapturedCandidate candidate) => candidate.Kind switch
    {
        "name" or "nameValue" or "id" or "select" or "testid" => !UnstableId.IsUnstable(candidate.Value),
        "role" or "roleInGroup" or "row" =>
            candidate.Parts.All(part => part.M != "role" || AriaRoleName(part.Role) is not null),
        "generator" => !string.IsNullOrWhiteSpace(candidate.Csharp),
        _ => false,
    };

    private static IEnumerable<string> CssStrings(string code) =>
        CssLiteral().Matches(code).Select(match => Regex.Unescape(match.Groups["css"].Value));

    // A class selector: a dot that starts a name, outside the attribute values in brackets.
    private static bool HasClass(string css) => ClassSelector().IsMatch(AttributeValue().Replace(css, "[]"));

    private static IEnumerable<string> IdsIn(string css) =>
        IdSelector().Matches(css.Replace("\\", "")).Select(match => match.Groups["id"].Value);

    [GeneratedRegex(@"\.Nth\(|\.First\b|\.Last\b|nth-child|nth-of-type|>> nth=")]
    private static partial Regex Position();

    [GeneratedRegex("""Locator\("(?<css>(?:[^"\\]|\\.)*)"\)""")]
    private static partial Regex CssLiteral();

    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex AttributeValue();

    [GeneratedRegex(@"(^|[\s>+~(,*a-zA-Z0-9\]])\.-?[_a-zA-Z]")]
    private static partial Regex ClassSelector();

    [GeneratedRegex("""#(?<id>[^\s.\[>+~:]+(?::[^\s.\[>+~]*)?)|\[id=["'](?<id>[^"']*)["']\]""")]
    private static partial Regex IdSelector();
}
