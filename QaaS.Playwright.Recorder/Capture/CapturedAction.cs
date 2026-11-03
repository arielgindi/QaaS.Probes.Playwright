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
    /// <c>upload</c>, <c>drag</c>, <c>navigate</c>, <c>newTab</c>, or <c>expect</c>: a check the person chose (F6),
    /// which the flow makes after the action before it.
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

    /// <summary>
    /// For a choice, the click just before it that only opened its list, on the field or on what holds it, by its id:
    /// the flow opens the list as the choice is written.
    /// </summary>
    public string? Opener { get; set; }

    /// <summary>
    /// The list the element was in, scrolled to it, which renders only the items in sight (a virtualised list): the flow
    /// scrolls it as far first.
    /// </summary>
    public CapturedScroll? Scrolled { get; set; }

    /// <summary>For a drag, the element it was dropped on.</summary>
    public CapturedTarget? To { get; set; }

    /// <summary>The item was in a menu that opened without a click, as one that opens on hover.</summary>
    public bool? HoverMenu { get; set; }

    /// <summary>For an item of a menu that opened on hover: what the pointer was on as it opened, which the flow hovers first.</summary>
    public CapturedTarget? HoverOpener { get; set; }

    /// <summary>
    /// For a choice of an option with no role, as antd's virtual list draws them: the CSS of each option of its kind, which
    /// the flow finds the option in by its text, among those that show.
    /// </summary>
    public string? OptionCss { get; set; }

    /// <summary>For a date picked in a calendar: the picker kept it only once its OK, or Enter, committed it.</summary>
    public bool? Committed { get; set; }

    /// <summary>For a click: it left the focus in a text field, the element itself or one in it.</summary>
    public bool? Focus { get; set; }

    /// <summary>
    /// For a click: the page was watched for what changed after it, so an action without a <c>changed</c> effect changed
    /// nothing. A recording of an older recorder has no such watch.
    /// </summary>
    public bool? Watched { get; set; }

    public List<CapturedEffect> Effects { get; set; } = [];

    /// <summary>Added in the panel rather than done in the app, so it caused nothing.</summary>
    public bool Added { get; set; }

    /// <summary>
    /// Not seen but guessed by the recorder: an Escape that closes a dialog that closed with no action seen. Never in a
    /// recording; the planner makes it.
    /// </summary>
    [JsonIgnore]
    public bool Guessed { get; set; }

    /// <summary>
    /// For a navigation or a tab: where the page was when the person resumed a paused recording, having changed while it
    /// was paused.
    /// </summary>
    public bool? AfterPause { get; set; }

    /// <summary>
    /// For a check: what it expects of <see cref="Target"/>: <c>visible</c>, <c>hidden</c>, <c>text</c>, <c>contains</c>,
    /// <c>value</c>, <c>checked</c>, <c>unchecked</c>, <c>enabled</c> or <c>disabled</c>; of <see cref="Group"/>,
    /// <c>count</c> or <c>row</c>; or of the page, <c>url</c>. <see cref="Value"/> holds the text, value, count or
    /// address expected.
    /// </summary>
    public string? Check { get; set; }

    /// <summary>
    /// For a check of a count, the locators that find every item the element is one of (a list's items, a table's rows);
    /// of a row, the row by the cells chosen.
    /// </summary>
    public CapturedTarget? Group { get; set; }

    /// <summary>What the person changed about the action in the panel; null when nothing.</summary>
    public ActionEdit? Edit { get; set; }

    /// <summary>The element the flow finds to act on: for a choice, its field.</summary>
    [JsonIgnore]
    public CapturedTarget? Element => Kind == "choose" ? Field ?? Target : Target;
}

/// <summary>
/// What the person changed in the panel about an action. It is part of the recording, so a flow planned again, or a
/// recording saved and read back, keeps it.
/// </summary>
internal sealed class ActionEdit
{
    /// <summary>The name the person gave the step (method) the action is the first of, e.g. <c>SaveTicketAsync</c>.</summary>
    public string? Step { get; set; }

    /// <summary>The auto checks of the action the person dropped, by their key (<c>alert:...</c>, <c>url</c>, ...).</summary>
    public List<string>? Dropped { get; set; }

    /// <summary>
    /// The person's changes to the settings the action reads, by the value's part in it: <c>value</c>, <c>search</c>,
    /// <c>option</c>, <c>shown</c> or <c>row</c>.
    /// </summary>
    public Dictionary<string, ValueEdit>? Values { get; set; }
}

/// <summary>A setting as the person changed it: its name, its default, and how the flow types it.</summary>
internal sealed class ValueEdit
{
    /// <summary>The setting's name, instead of the one the recorder gave it.</summary>
    public string? Name { get; set; }

    /// <summary>Its default, instead of the value recorded; for a date relative to today, the days from today.</summary>
    public string? Value { get; set; }

    /// <summary>The flow appends its run's stamp to it, so it never repeats from run to run (a new record's name).</summary>
    public bool? Unique { get; set; }

    /// <summary>
    /// A date the flow types relative to the day it runs, in this format (<c>dd/MM/yyyy</c>); <see cref="Value"/> holds
    /// the days from today.
    /// </summary>
    public string? DateFormat { get; set; }
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

    /// <summary>
    /// The label a person reads beside the element, though nothing ties it to it: the label of its form group, of its row
    /// or column, or the text its <c>aria-describedby</c> points at, such as a select's "Select State".
    /// </summary>
    public string? GroupLabel { get; set; }

    /// <summary>The placeholder of a field, which its accessible name may come from.</summary>
    public string? Placeholder { get; set; }

    /// <summary>The heading of the section the element is in, when the page has more than one: "Personal Details".</summary>
    public string? Section { get; set; }

    /// <summary>For an element with no text, the word its icon says: <c>trash</c>, <c>pencil</c>, <c>plus</c>, <c>heart</c>.</summary>
    public string? Icon { get; set; }

    /// <summary>A field the person can type in: a text input that is not read-only, a text area, an editable element.</summary>
    public bool? Editable { get; set; }

    /// <summary>A checkbox or radio under the box a library draws over it, which a click at its middle lands on.</summary>
    public bool? Covered { get; set; }

    public bool Interactive { get; set; }

    /// <summary>One of a list of like items, or in one: an option, a card among cards, a link of a menu.</summary>
    public bool? Item { get; set; }

    public CapturedRow? Row { get; set; }
    public List<CapturedCandidate> Candidates { get; set; } = [];

    /// <summary>The candidate the person chose in the recorder's panel, by its index; null leaves it to the policy.</summary>
    public int? Chosen { get; set; }
}

/// <summary>A list scrolled to an element, which renders only the items in sight.</summary>
internal sealed class CapturedScroll
{
    /// <summary>How far down it was scrolled, in pixels.</summary>
    public int Top { get; set; }

    public CapturedTarget? List { get; set; }
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
    /// <summary><c>css</c>, <c>role</c>, <c>testid</c>, <c>text</c> or <c>filter</c>.</summary>
    public string M { get; set; } = "";

    /// <summary>For <c>text</c>: the exact text of the element (<c>GetByText</c>).</summary>
    public string? Text { get; set; }

    /// <summary>
    /// For a dialog that has no name: the heading it shows, which it is found by
    /// (<c>GetByRole(AriaRole.Dialog).Filter(new() { Has = page.GetByText(heading) })</c>).
    /// </summary>
    public string? Heading { get; set; }

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
    /// <summary>
    /// The title without bidi marks and asterisk: its accessible name, else the heading it shows
    /// (<see cref="Heading"/>).
    /// </summary>
    public string Title { get; set; } = "";

    public string? Raw { get; set; }
    public bool TitleMatches { get; set; }

    /// <summary>The title up to its first word with a digit, when it has one: "Close HD-1041" gives "Close".</summary>
    public string? Prefix { get; set; }

    public bool PrefixMatches { get; set; }
    public bool RawMatches { get; set; }

    /// <summary>
    /// For a dialog that has no accessible name, as a hand-made modal often has: the text it shows as its title, its
    /// heading, else its first words.
    /// </summary>
    public string? Heading { get; set; }

    /// <summary>The dialog with an element of exactly <see cref="Heading"/>'s text is this one alone.</summary>
    public bool HeadingMatches { get; set; }

    public bool UnnamedMatches { get; set; }
}

/// <summary>What the page did a moment after an action.</summary>
internal sealed class CapturedEffect
{
    /// <summary>
    /// <c>dialogOpened</c>, <c>dialogClosed</c>, <c>alert</c>, <c>tabSelected</c>, <c>settled</c> (a list closed and
    /// its field shows the choice), <c>changed</c> (the page added, removed or changed something after a click),
    /// <c>busy</c> (a busy indicator came up and went away: <see cref="Value"/> is
    /// <c>progressbar</c> or <c>ariaBusy</c>), <c>urlChanged</c>, <c>popup</c> or <c>jsDialog</c>.
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

    /// <summary>For <c>settled</c>: the field holds several choices, as a multi-select does, each one removable.</summary>
    public bool? Multi { get; set; }
}
