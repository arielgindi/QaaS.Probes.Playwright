using System.Text.RegularExpressions;
using Microsoft.Playwright;
using QaaS.Playwright.Recorder.Capture;

namespace QaaS.Playwright.Recorder.Generation;

/// <summary>
/// Picks the locator of an element from the ones the capture script checked, and writes it as C#. In order, the first
/// that found the element alone wins: its Formik path (<c>[name='x']</c>), its own stable id, a MUI Select's field by
/// the path of its hidden input, the test id, its role and exact name, its row, the form group its label names, the
/// section its heading names, its title, its icon, the card its words tell apart, the nearest stable id around it, for
/// an element without a role (a card, an option of a hand-made list) the element of its label's text in it, the place it
/// has in what holds it (the checkbox of a table's header), and last Playwright's own generator. Never a class a
/// CSS-in-JS library hashed, a position, an empty name, a text glued from several elements, or a row's whole name.
/// </summary>
internal static partial class LocatorPolicy
{
    private static readonly string[] Order =
    [
        "name", "id", "select", "testid", "role", "roleInGroup", "row", "group", "section", "title", "icon", "item", "within",
        "text", "structure", "generator",
    ];

    // A choice among radios is written with the option's name, so the name can come from the flow's settings.
    private static readonly string[] ByName = ["role", "roleInGroup", "nameValue"];

    /// <summary>
    /// The locator to write for the element; <paramref name="byName"/> prefers one that finds it by its name (a
    /// radio of a group). Null when nothing was captured.
    /// </summary>
    public static CapturedCandidate? Choose(CapturedTarget? target, bool byName = false)
    {
        if (target is null) return null;
        byName |= IsNumberedToggle(target);
        if (target.Chosen is { } chosen && target.Candidates.ElementAtOrDefault(chosen) is { } picked && Writable(picked)) return picked;
        // A field named only by its placeholder, a prompt (Type for hints...) or an example of a value (yyyy-dd-mm), is
        // found by its group's label when it has one, and never by an example.
        var grouped = target.Candidates.Any(candidate => candidate is { Kind: "group", Matches: true } && IsUsable(candidate));
        var usable = target.Candidates.Where(candidate => candidate.Matches && IsUsable(candidate)
                                                          && !(candidate.Kind == "role" && candidate.Text is { } said
                                                               && said == target.Placeholder && (grouped || Names.IsExample(said)))).ToList();
        foreach (var kind in byName ? [.. ByName, .. Order] : Order)
            if (usable.FirstOrDefault(candidate => candidate.Kind == kind) is { } found)
                return found;

        // Nothing good found the element alone: Playwright's own, which finds it, with what is wrong with it to check by
        // hand; else the closest miss, which the flow's reader must fix.
        // Never one by a title or a text no person wrote, nor by a generated id, while another comes close.
        var generated = target.Candidates.FirstOrDefault(candidate => candidate is { Kind: "generator", Matches: true } && Writable(candidate));
        var closest = target.Candidates.Where(IsUsable).OrderBy(candidate => candidate.Count <= 0 ? int.MaxValue : candidate.Count).FirstOrDefault();
        return generated is not null && (closest is null || !Unfit(generated)) ? generated : closest ?? generated;
    }

    // Playwright's own locator by what changes each load or names nothing: a generated id, a title no person wrote.
    private static bool Unfit(CapturedCandidate generated) =>
        JunkInCode().IsMatch(generated.Csharp ?? "") || CssStrings(generated.Csharp ?? "").SelectMany(IdsIn).Any(UnstableId.IsUnstable);

    /// <summary>
    /// A checkbox or switch whose id only numbers it among others (<c>hobbies-checkbox-3</c>): found by its label, as a
    /// person reads it, which a loop over the group's choices can also find each by.
    /// </summary>
    public static bool IsNumberedToggle(CapturedTarget? target) =>
        target is { Role: "checkbox" or "switch", Text.Length: > 0, Id: { } id } && NumberedId().IsMatch(id);

    /// <summary>
    /// The element to locate a click by: <paramref name="target"/>, unless only its label's text or Playwright's own
    /// locator finds it, or nothing finds it alone, and one of <paramref name="equivalents"/>, which the same click hits,
    /// has a stable id or test id that finds it alone, else is the one control in it (the input in a select's box) found
    /// by its role or name; then the first such.
    /// </summary>
    public static CapturedTarget? Prefer(CapturedTarget? target, IEnumerable<CapturedTarget>? equivalents)
    {
        if (target is null || equivalents is null || Choose(target) is { Kind: not ("generator" or "text"), Matches: true })
            return target;
        var others = equivalents.ToList();
        return others.FirstOrDefault(other => Choose(other) is { Kind: "id" or "testid", Matches: true })
               ?? others.FirstOrDefault(other => other.Interactive && Choose(other) is { Kind: "name" or "role", Matches: true })
               ?? target;
    }

    /// <summary>
    /// Why each locator the policy tries before <paramref name="chosen"/> lost, as <c>kind: reason</c>, e.g.
    /// <c>id: found 2 (1 hidden)</c>, <c>id: looks generated</c> or <c>testid: none</c>. Kinds that do not apply
    /// to the element (a form name on a button, a table row) are left out.
    /// </summary>
    public static IReadOnlyList<string> WhyNot(CapturedTarget target, CapturedCandidate? chosen, bool byName = false)
    {
        var reasons = new List<string>();
        byName |= IsNumberedToggle(target);
        foreach (var kind in (byName ? [.. ByName, .. Order] : Order).Distinct())
        {
            if (chosen is { Matches: true } && chosen.Kind == kind) break;
            var tried = target.Candidates.FirstOrDefault(candidate => candidate.Kind == kind);
            if ((tried is null ? Missing(kind, target) : WhyLost(tried)) is { } reason) reasons.Add($"{kind}: {reason}");
        }

        return reasons;
    }

    /// <summary>
    /// The locator as a person reads it, without the dialog it is found in: <c>[name='x']</c>, <c>button "Save"</c>,
    /// <c>row "HL-775-ZE" » button "Actions"</c>; Playwright's own as its C#.
    /// </summary>
    public static string? Describe(CapturedCandidate? candidate)
    {
        if (candidate is null) return null;
        if (candidate.Kind == "generator") return candidate.Csharp;
        var parts = candidate.Parts.SkipWhile(part => part is { M: "role", Role: "dialog" }).Select(part => part.M switch
        {
            "css" => part.Css,
            "testid" => $"test id \"{part.Value}\"",
            "text" => $"text \"{part.Text}\"",
            "filter" => $"\"{part.HasText}\"",
            "has" => $"has \"{part.Text}\"",
            "title" => $"title \"{part.Text}\"",
            _ => part.Regex is not null ? $"{part.Role} /{part.Regex}/" : part.Name is not null ? $"{part.Role} \"{part.Name}\"" : part.Role,
        });
        return string.Join(" » ", parts);
    }

    /// <summary>What <see cref="Problem"/> says of Playwright's locator by an id made anew on each load.</summary>
    public const string GeneratedIdProblem = "Playwright's locator uses a generated id";

    /// <summary>Why the locator needs a person's look, or null when it is fine.</summary>
    public static string? Problem(CapturedCandidate? candidate)
    {
        if (candidate is null) return "no locator was captured for this element";
        if (!candidate.Matches)
            return candidate.Count > 1
                ? $"this locator found {candidate.Count} elements when recorded"
                : "this locator did not find the element when recorded";
        if (candidate.Parts.FirstOrDefault(part => part is { M: "role", Regex: null } && HoldsVolatile(part.Name)) is { } changing)
            return $"its name holds what may change from run to run (\"{changing.Name}\"): find it by what stays";
        if (candidate.Kind != "generator") return null;
        var code = candidate.Csharp ?? "";
        if (Position().IsMatch(code)) return "Playwright's locator picks the element by its position";
        if (EmptyName().IsMatch(code)) return "Playwright's locator matches an empty name, which many elements have";
        if (CssStrings(code).Any(HasHashedClass)) return "Playwright's locator picks the element by a class a style library generated";
        if (CssStrings(code).Any(HasClass)) return "Playwright's locator picks the element by a style class";
        if (CssStrings(code).SelectMany(IdsIn).Any(UnstableId.IsUnstable)) return GeneratedIdProblem;
        if (Texts(code).Any(Glued)) return "Playwright's locator finds a wrapper by the text of everything in it";
        if (Texts(code).Any(text => !text.Any(char.IsLetter))) return "Playwright's locator finds the element by a number alone";
        if (WholeRowName().IsMatch(code)) return "Playwright's locator finds the row by all it says, not by its key";
        if (code.Any(character => character is >= '\uE000' and <= '\uF8FF'))
            return "Playwright's locator names the element with the character of an icon font";
        return null;
    }

    /// <summary>
    /// The locator as C#, from <paramref name="root"/> (<c>page</c>, <c>dialog</c>, ...). A part's name, text or CSS can
    /// be replaced by an expression, e.g. <c>Configuration.VisitType</c>. <paramref name="breakBefore"/> goes before each
    /// call, where a long line may break.
    /// </summary>
    public static string Render(string root, IEnumerable<LocatorPart> parts, Func<LocatorPart, string?>? nameOf = null,
        string breakBefore = "") =>
        root + string.Concat(parts.Select(part => breakBefore + "." + RenderPart(part, nameOf?.Invoke(part), root, breakBefore)));

    /// <summary>An attribute's value as the capture script quotes it in CSS: <c>[value='x']</c>.</summary>
    public static string CssValue(string value) =>
        $"[value='{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)}']";

    public static string RenderGenerated(string root, CapturedCandidate generated) => $"{root}.{generated.Csharp}";

    /// <summary>The role's member of <see cref="AriaRole"/>, e.g. <c>Menuitem</c>; null for a role it does not have.</summary>
    public static string? AriaRoleName(string? role) =>
        role is { Length: > 0 } && role.All(char.IsAsciiLetter) && Enum.TryParse<AriaRole>(role, ignoreCase: true, out var value)
            ? value.ToString()
            : null;

    // A dialog found by its heading is the first part of a locator, so its root is a page; so is a group found by its label.
    private static string RenderPart(LocatorPart part, string? name, string root, string breakBefore) => part.M switch
    {
        "css" => $"Locator({name ?? CSharp.Literal(part.Css ?? "")})",
        "testid" => $"GetByTestId({CSharp.Literal(part.Value ?? "")})",
        "text" => $"GetByText({name ?? CSharp.Literal(part.Text ?? "")}, new() {{ Exact = true }})",
        "title" => $"GetByTitle({name ?? CSharp.Literal(part.Text ?? "")}, new() {{ Exact = true }})",
        "filter" => $"Filter(new() {{ HasText = {name ?? CSharp.Literal(part.HasText ?? "")} }})",
        "has" => $"Filter(new() {{ Has = {PageOf(root)}.GetByText({name ?? CSharp.Literal(part.Text ?? "")}, new() {{ Exact = true }}) }})",
        "role" when part.Heading is { } heading =>
            $"GetByRole(AriaRole.{AriaRoleName(part.Role)}){breakBefore}.Filter(new() {{ Has = {root}.GetByText({name ?? CSharp.Literal(heading)}, new() {{ Exact = true }}) }})",
        "role" => RenderRole(part, name),
        _ => throw new InvalidOperationException($"Unknown locator part '{part.M}'."),
    };

    // A locator inside Has is found from the page, as a page or a dialog's page has: dialog.Page, page.
    private static string PageOf(string root) => root is "dialog" or "opened" ? root + ".Page" : root.EndsWith("Dialog", StringComparison.Ordinal) ? root + ".Page" : root;

    private static string RenderRole(LocatorPart part, string? name)
    {
        var role = $"AriaRole.{AriaRoleName(part.Role)}";
        if (part.Regex is not null) return $"GetByRole({role}, new() {{ NameRegex = new Regex({CSharp.Literal(part.Regex)}) }})";
        if (name is null && SteadyName(part.Name) is { } steady) return $"GetByRole({role}, new() {{ NameRegex = new Regex({CSharp.Literal(steady)}) }})";
        if (part.Name is null) return $"GetByRole({role})";
        // A name read from a setting without the icon or avatar it starts with is matched in it, as part of it.
        var exact = part.Exact && (name is null || WithoutIcon(part.Name) == part.Name) ? ", Exact = true" : "";
        return $"GetByRole({role}, new() {{ Name = {name ?? CSharp.Literal(part.Name)}{exact} }})";
    }

    /// <summary>
    /// A name an icon's label starts, as antd's buttons say "plus New", or "loading Next" while a spinner turns in them, or
    /// that a flag's label doubles ("EnglishEnglish"), as a pattern that finds the element with it or without it, as the
    /// icon or the spinner comes and goes; null for any other name.
    /// </summary>
    public static string? SteadyName(string? name)
    {
        if (name is null) return null;
        if (IconPrefix().Match(name) is { Success: true } icon)
            return $"^(?:{PatternOf(icon.Groups["icon"].Value)} )?{PatternOf(icon.Groups["rest"].Value)}$";
        // A person's name after the initial of their avatar: "L Leonie Abernathy".
        if (AvatarInitial().Match(name) is { Success: true } avatar)
            return $"^(?:{PatternOf(avatar.Groups["initial"].Value)} )?{PatternOf(avatar.Groups["rest"].Value)}$";
        var half = name.Length / 2;
        return name.Length >= 6 && name.Length % 2 == 0 && name[..half] == name[half..] && !name[..half].Contains(' ')
            ? $"^(?:{PatternOf(name[..half])})?{PatternOf(name[..half])}$"
            : null;
    }

    /// <summary>The name without the icon's label it starts with, as a person reads it: "plus New" is New.</summary>
    public static string WithoutIcon(string name) =>
        IconPrefix().Match(name) is { Success: true } icon ? icon.Groups["rest"].Value
        : AvatarInitial().Match(name) is { Success: true } avatar ? avatar.Groups["rest"].Value
        : name.Length >= 6 && name.Length % 2 == 0 && name[..(name.Length / 2)] == name[(name.Length / 2)..] ? name[..(name.Length / 2)]
        : name;

    /// <summary>
    /// A title or text no person wrote: what a page shows of an object or of nothing (<c>[object Object]</c>,
    /// <c>undefined</c>).
    /// </summary>
    public static bool IsJunk(string? text) => text is not null && JunkText().IsMatch(text);

    private static string PatternOf(string text) => CSharp.Pattern(text);

    /// <summary>Whether the candidate is stable enough to be a locator at all, whatever it found.</summary>
    public static bool CanUse(CapturedCandidate candidate) => IsUsable(candidate);

    // The kinds of a locator's parts that Render writes.
    private static readonly HashSet<string> PartKinds = ["css", "testid", "text", "title", "filter", "has", "role"];

    // A locator that can be written as C#: of parts Render knows, each role one of AriaRole's; Playwright's own, its C#.
    // A recording of another version, or from a file, may hold one of neither.
    // The first part finds elements, as a filter of none cannot.
    private static bool Writable(CapturedCandidate candidate) => candidate.Kind == "generator"
        ? candidate.Csharp is { } csharp && GeneratedCall().IsMatch(csharp)
        : candidate.Parts is [{ M: not ("filter" or "has") }, ..]
          && candidate.Parts.All(part => PartKinds.Contains(part.M) && (part.M != "role" || AriaRoleName(part.Role) is not null));

    // Playwright's C# of a locator: a call of a page's, GetByRole(...), Locator(...), and the calls after it.
    [GeneratedRegex(@"^[A-Z]\w*\(.*\)$", RegexOptions.Singleline)]
    private static partial Regex GeneratedCall();

    private static bool IsUsable(CapturedCandidate candidate) => Writable(candidate) && !WholeRow(candidate) && candidate.Kind switch
    {
        "name" or "nameValue" or "id" or "select" or "testid" or "within" => !UnstableId.IsUnstable(candidate.Value) && CssIsStable(candidate),
        "role" or "roleInGroup" or "row" or "group" or "section" or "item" or "icon" or "title" or "structure" =>
            candidate.Parts.All(part => part.M != "role" || AriaRoleName(part.Role) is not null) && CssIsStable(candidate)
            && candidate.Parts.All(part => part is not ({ M: "has" or "title", Text: null or "" } or { M: "filter", HasText: "" }))
            && !candidate.Parts.Any(part => IsJunk(part.Text) || IsJunk(part.Name)),
        "text" => candidate.Parts.Any(part => part is { M: "text", Text.Length: > 0 }) && CssIsStable(candidate)
                  && !candidate.Parts.Any(part => IsJunk(part.Text)),
        "generator" => !string.IsNullOrWhiteSpace(candidate.Csharp) && Problem(candidate) is null && !JunkInCode().IsMatch(candidate.Csharp),
        _ => false,
    };

    // A row by all it says, its counts, statuses and dates with it, which change: never, as its key finds it.
    private static bool WholeRow(CapturedCandidate candidate) =>
        candidate.Kind == "role" && candidate.Parts.Any(part => part is { M: "role", Role: "row", Name.Length: > 0 });

    /// <summary>
    /// Whether a name holds what changes from load to load or what a flow may edit: a price, a date, a time, a percentage.
    /// </summary>
    public static bool HoldsVolatile(string? name) => name is not null && VolatileInName().IsMatch(name);

    // A class that a CSS-in-JS library hashed changes with each build, and so does the chain it is in.
    private static bool CssIsStable(CapturedCandidate candidate) =>
        candidate.Parts.All(part => part.M != "css" || part.Css is not { } css || !HasHashedClass(css));

    /// <summary>Whether a CSS selector names a class a style library generated: <c>.css-1x2y3z-control</c>, <c>.sc-bdVaJa</c>.</summary>
    public static bool HasHashedClass(string css) =>
        ClassName().Matches(AttributeValue().Replace(css, "[]")).Any(match => StyleClass.IsHashed(match.Groups["class"].Value));

    // The texts the generator finds by: GetByText("..."), HasText = "...", a regex's.
    private static IEnumerable<string> Texts(string code) =>
        GeneratedText().Matches(code).Select(match => Regex.Unescape(match.Groups["text"].Value).Trim('^', '$'));

    // Words of several elements run together: "Employee Full NameEmployee", "Blood TypeB-", "202519701971".
    private static bool Glued(string text) =>
        GluedWords().IsMatch(text) || GluedNumbers().IsMatch(text);

    // A kind the capture script did not try: one the element has nothing for.
    private static string? Missing(string kind, CapturedTarget target) => kind switch
    {
        "id" or "testid" or "generator" => "none",
        "name" when target.Tag is "input" or "textarea" or "select" => "none",
        "role" => target.Role is null ? "none" : "no name",
        _ => null,
    };

    /// <summary>
    /// Why the candidate cannot be the locator, e.g. <c>found 2 (1 hidden)</c>, <c>found another element</c> or
    /// <c>looks generated</c>; null when it found the element alone.
    /// </summary>
    public static string? WhyLost(CapturedCandidate candidate)
    {
        if (!IsUsable(candidate))
            return candidate.Kind switch
            {
                "generator" => "none",
                _ when !Writable(candidate) => "not a locator the recorder can write",
                _ when WholeRow(candidate) => "finds the row by all it says",
                _ when candidate.Parts.Any(part => IsJunk(part.Text) || IsJunk(part.Name)) => "a text no person wrote",
                "role" or "roleInGroup" or "row" when candidate.Parts.FirstOrDefault(part => part.M == "role" && AriaRoleName(part.Role) is null) is { } unknown =>
                    $"no AriaRole for '{unknown.Role}'",
                _ => "looks generated",
            };
        return candidate switch
        {
            { Matches: true } => null,
            { Count: < 0 } => "not checked",
            { Count: 0 } => "found none",
            { Count: 1 } => "found another element",
            { Hidden: > 0 } => $"found {candidate.Count} ({candidate.Hidden} hidden)",
            _ => $"found {candidate.Count}",
        };
    }

    private static IEnumerable<string> CssStrings(string code) =>
        CssLiteral().Matches(code).Select(match => Regex.Unescape(match.Groups["css"].Value));

    // A class selector: a dot that starts a name, outside the attribute values in brackets.
    private static bool HasClass(string css) => ClassSelector().IsMatch(AttributeValue().Replace(css, "[]"));

    private static IEnumerable<string> IdsIn(string css) =>
        IdSelector().Matches(css).Select(match => UnstableId.CssUnescape(match.Groups["id"].Value));

    [GeneratedRegex(@"[-_]\d+$")]
    private static partial Regex NumberedId();

    // An icon's label in lowercase, then the words a person reads, in capitals or another script.
    [GeneratedRegex(@"^(?<icon>[a-z]+(?:-[a-z]+)*) (?<rest>[^\p{Ll}\s].*)$")]
    private static partial Regex IconPrefix();

    // The initial an avatar shows, then the name it is the initial of.
    [GeneratedRegex(@"^(?<initial>\p{Lu}) (?<rest>\k<initial>\p{Ll}+(?: \p{L}[\p{L}'-]*)+)$")]
    private static partial Regex AvatarInitial();

    [GeneratedRegex(@"^\s*(\[object \w+\]|undefined|null|NaN)\s*$")]
    private static partial Regex JunkText();

    [GeneratedRegex(@"""\[object \w+\]""|""undefined""")]
    private static partial Regex JunkInCode();


    // A price, a date, a time, a percentage; a weekday or a month's day, as a calendar's event names its time.
    [GeneratedRegex(@"[-+]?(\p{Sc}|\b[A-Z]{1,3}\$)\s?\d|\d[\d.,]*\s?\p{Sc}|\d{1,4}[/.-]\d{1,2}[/.-]\d{1,4}|\b\d{1,2}:\d{2}|\b\d+([.,]\d+)?\s?%" +
                    @"|\b(mon|tue|wed|thu|fri|sat|sun)[a-z]*day\b|\b(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\.?\s\d{1,2}\b", RegexOptions.IgnoreCase)]
    private static partial Regex VolatileInName();

    [GeneratedRegex(@"\.Nth\(|\.First\b|\.Last\b|nth-child|nth-of-type|>> nth=")]
    private static partial Regex Position();

    [GeneratedRegex(@"Name = ""[\s\uE000-\uF8FF]*""|GetBy(Label|Text|Title|Placeholder|AltText)\(""[\s\uE000-\uF8FF]*""")]
    private static partial Regex EmptyName();

    [GeneratedRegex("""GetByRole\(AriaRole\.Row, new\(\) \{ Name = """)]
    private static partial Regex WholeRowName();

    [GeneratedRegex(@"(?:GetByText\(|HasText = |HasTextRegex = new Regex\()""(?<text>(?:[^""\\]|\\.)*)""")]
    private static partial Regex GeneratedText();

    [GeneratedRegex(@"\p{Ll}\p{Lu}\p{Ll}")]
    private static partial Regex GluedWords();

    [GeneratedRegex(@"\d{4}\d{4}\d{4}")]
    private static partial Regex GluedNumbers();

    [GeneratedRegex(@"\.(?<class>-?[_a-zA-Z][\w-]*)")]
    private static partial Regex ClassName();

    [GeneratedRegex("""Locator\("(?<css>(?:[^"\\]|\\.)*)"\)""")]
    private static partial Regex CssLiteral();

    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex AttributeValue();

    [GeneratedRegex(@"(^|[\s>+~(,*a-zA-Z0-9\]])\.-?[_a-zA-Z]")]
    private static partial Regex ClassSelector();

    [GeneratedRegex("""#(?<id>[^\s.\[>+~:]+(?::[^\s.\[>+~]*)?)|\[id=["'](?<id>[^"']*)["']\]""")]
    private static partial Regex IdSelector();
}
