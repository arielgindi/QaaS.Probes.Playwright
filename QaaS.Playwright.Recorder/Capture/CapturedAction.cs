using System.Text.Json.Serialization;

namespace QaaS.Playwright.Recorder.Capture;

/// <summary>
/// One thing a person did in the recorded page, as the capture script described it when it happened: what kind of
/// action, every way to find its element (each checked against the page), the dialog it was in, the value the field
/// settled on, and what the page did within a moment of it.
/// </summary>
internal sealed class CapturedAction
{
    public string Id { get; set; } = "";

    /// <summary>When it began, in Unix milliseconds: the pointer went down, or the first key was typed.</summary>
    public long T { get; set; }

    /// <summary>
    /// <c>click</c>, <c>dblclick</c>, <c>fill</c>, <c>check</c>, <c>uncheck</c>, <c>choose</c>, <c>press</c>,
    /// <c>upload</c>, <c>drag</c>, <c>navigate</c> or <c>newTab</c>.
    /// </summary>
    public string Kind { get; set; } = "";

    /// <summary>The page it happened in: <c>page</c>, or <c>page1</c>, ... for pop-ups and tabs.</summary>
    public string Page { get; set; } = "page";

    /// <summary>
    /// What the element is beyond its role: <c>select</c>, <c>select-open</c>, <c>autocomplete</c>,
    /// <c>autocomplete-button</c>, <c>native</c>, <c>option</c>, <c>radio</c>, <c>checkbox</c>, <c>switch</c>,
    /// <c>tab</c>, <c>menuitem</c>, <c>date</c>, <c>text</c> or <c>textarea</c>.
    /// </summary>
    public string? Widget { get; set; }

    /// <summary>
    /// A fill's settled value without bidi marks, or the text of the option chosen. For a fill, the value the field
    /// settled on, after the app re-formatted it.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>A fill's value as the field holds it, bidi marks included.</summary>
    public string? Raw { get; set; }

    /// <summary>False while typing goes on; the recorder keeps the last report of each action.</summary>
    public bool? Final { get; set; }

    /// <summary>A date chosen in a picker's calendar, recorded as typed into its field.</summary>
    public bool? Picked { get; set; }

    public bool? Checked { get; set; }

    /// <summary>The key of a press: <c>Enter</c> or <c>Escape</c>.</summary>
    public string? Key { get; set; }

    /// <summary>A press with the focus on no element.</summary>
    public bool? OnPage { get; set; }

    /// <summary>The names of the files chosen for an upload; the browser does not tell their folder.</summary>
    public List<string>? Files { get; set; }

    /// <summary>The page's address when it happened, or where a navigation went.</summary>
    public string? Url { get; set; }

    public CapturedDialog? Dialog { get; set; }

    /// <summary>The titles of the dialogs that were open when it happened.</summary>
    public List<string>? OpenDialogs { get; set; }

    /// <summary>The element acted on; for a choice, the field the list belongs to.</summary>
    public CapturedTarget? Target { get; set; }

    /// <summary>
    /// For a click, other elements it hits that have an id or a test id: between the element the pointer hit and the
    /// target, or a wrapper's only child. When the recorder locates the click by one of them instead
    /// (<see cref="Generation.LocatorPolicy.Prefer"/>), that one is the target and the element the page reported is
    /// listed here.
    /// </summary>
    public List<CapturedTarget>? Equivalents { get; set; }

    /// <summary>For a choice, the Select or Autocomplete the option belongs to.</summary>
    public CapturedTarget? Field { get; set; }

    /// <summary>For a choice, the option clicked.</summary>
    public CapturedTarget? Option { get; set; }

    /// <summary>For a drag, the element it was dropped on.</summary>
    public CapturedTarget? To { get; set; }

    /// <summary>The item was in a menu that opened without a click, as one that opens on hover.</summary>
    public bool? HoverMenu { get; set; }

    public List<CapturedEffect> Effects { get; set; } = [];

    /// <summary>Added in the panel rather than done in the app, so it caused nothing.</summary>
    public bool Added { get; set; }

    /// <summary>
    /// For a navigation or a tab: where the page was when the person resumed a paused recording, having changed while it
    /// was paused.
    /// </summary>
    public bool? AfterPause { get; set; }

    /// <summary>The element the flow finds to act on: for a choice, its field.</summary>
    [JsonIgnore]
    public CapturedTarget? Element => Kind == "choose" ? Field ?? Target : Target;
}

/// <summary>An element, with every locator that might find it.</summary>
internal sealed class CapturedTarget
{
    public string Tag { get; set; } = "";
    public string? Role { get; set; }
    public string? Type { get; set; }
    public string? Id { get; set; }

    /// <summary>The <c>name</c> attribute: with Formik, the field's path.</summary>
    public string? Name { get; set; }

    public string? TestId { get; set; }

    /// <summary>A checkbox's or radio's <c>value</c> attribute.</summary>
    public string? Value { get; set; }

    /// <summary>The accessible name without bidi marks and MUI's required asterisk.</summary>
    public string? Text { get; set; }

    public string? Label { get; set; }
    public bool Interactive { get; set; }
    public CapturedRow? Row { get; set; }
    public List<CapturedCandidate> Candidates { get; set; } = [];

    /// <summary>The candidate the person chose in the recorder's panel, by its index; null leaves it to the policy.</summary>
    public int? Chosen { get; set; }
}

/// <summary>The table row an element is in, and the text that tells the row apart from the others.</summary>
internal sealed class CapturedRow
{
    public string? Key { get; set; }

    /// <summary>The header of the key's column.</summary>
    public string? Header { get; set; }
}

/// <summary>A locator for an element, and what it found when the action happened.</summary>
internal sealed class CapturedCandidate
{
    /// <summary>
    /// <c>name</c>, <c>nameValue</c>, <c>id</c>, <c>select</c>, <c>testid</c>, <c>role</c>, <c>roleInGroup</c>,
    /// <c>row</c> or <c>generator</c>.
    /// </summary>
    public string Kind { get; set; } = "";

    /// <summary>The locator, from the page down, including the dialog it is scoped to.</summary>
    public List<LocatorPart> Parts { get; set; } = [];

    /// <summary>How many elements it found; -1 when it could not be checked.</summary>
    public int Count { get; set; }

    /// <summary>
    /// When it found more than one, how many of them were hidden: a copy a responsive layout keeps for another width.
    /// </summary>
    public int Hidden { get; set; }

    /// <summary>It found exactly the element acted on, and nothing else.</summary>
    public bool Matches { get; set; }

    /// <summary>The attribute value it is built on: the name, id or test id.</summary>
    public string? Value { get; set; }

    /// <summary>For a role locator, the name or the prefix it matches.</summary>
    public string? Text { get; set; }

    /// <summary>For the generator, Playwright's own C# locator.</summary>
    public string? Csharp { get; set; }

    /// <summary>For the generator, the same locator as a Playwright selector, to find the element from .NET.</summary>
    public string? Selector { get; set; }
}

/// <summary>One call of a locator chain.</summary>
internal sealed class LocatorPart
{
    /// <summary><c>css</c>, <c>role</c>, <c>testid</c> or <c>filter</c>.</summary>
    public string M { get; set; } = "";

    public string? Css { get; set; }
    public string? Value { get; set; }
    public string? HasText { get; set; }
    public string? Role { get; set; }
    public string? Name { get; set; }

    /// <summary>A name regex, in the syntax .NET and JavaScript share.</summary>
    public string? Regex { get; set; }

    public bool Exact { get; set; }

    /// <summary>The name keeps the page's bidi marks, as only that matched.</summary>
    public bool Marks { get; set; }
}

/// <summary>A dialog, and which of its names find it alone.</summary>
internal sealed class CapturedDialog
{
    /// <summary>The title without bidi marks and asterisk.</summary>
    public string Title { get; set; } = "";

    public string? Raw { get; set; }
    public bool TitleMatches { get; set; }

    /// <summary>The title up to its first word with a digit, when it has one: "Close HD-1041" gives "Close".</summary>
    public string? Prefix { get; set; }

    public bool PrefixMatches { get; set; }
    public bool RawMatches { get; set; }
    public bool UnnamedMatches { get; set; }
}

/// <summary>What the page did a moment after an action.</summary>
internal sealed class CapturedEffect
{
    /// <summary>
    /// <c>dialogOpened</c>, <c>dialogClosed</c>, <c>alert</c>, <c>tabSelected</c>, <c>settled</c> (a list closed and
    /// its field shows the choice), <c>urlChanged</c>, <c>popup</c> or <c>jsDialog</c>.
    /// </summary>
    public string Kind { get; set; } = "";

    public string? ActionId { get; set; }

    /// <summary>Milliseconds since the action it is reported for began.</summary>
    public long? SinceAction { get; set; }

    public long T { get; set; }
    public CapturedDialog? Dialog { get; set; }
    public string? Text { get; set; }

    /// <summary>How many alerts the page showed, this one included.</summary>
    public int? AlertCount { get; set; }

    public string? Value { get; set; }
    public string? Raw { get; set; }
    public string? Url { get; set; }
    public string? Page { get; set; }
}
