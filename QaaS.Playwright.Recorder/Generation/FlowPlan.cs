using QaaS.Playwright.Recorder.Capture;

namespace QaaS.Playwright.Recorder.Generation;

/// <summary>A recording turned into what the flow does: its steps, and the settings its values come from.</summary>
internal sealed class FlowPlan
{
    public required string ClassName { get; init; }
    public required string StartUrl { get; init; }

    /// <summary>The flow the recording started after, which a probe must run before it.</summary>
    public string? After { get; init; }

    /// <summary>Whether to write auto checks, as the person turned them in the panel; null as the caller says.</summary>
    public bool? AutoChecks { get; init; }

    public List<Step> Steps { get; } = [];

    /// <summary>The settings, in the order of the steps that read them.</summary>
    public List<Setting> Settings { get; } = [];

    /// <summary>What could not be recorded at all, for the person to do by hand.</summary>
    public List<string> NotRecorded { get; } = [];

    /// <summary>What the recorder suggests leaving out, which the person applies with a click (<see cref="Noise"/>).</summary>
    public List<Suggestion> Suggestions { get; } = [];
}

/// <summary>An action the flow does, with the locator chosen for its element and the settings it reads.</summary>
internal sealed class PlannedAction
{
    public required CapturedAction Source { get; init; }
    public string Kind => Source.Kind;

    /// <summary>The element acted on; for a choice, its field.</summary>
    public CapturedCandidate? Locator { get; set; }

    /// <summary>The locator as text, to tell whether two actions act on the same element.</summary>
    public string Key { get; set; } = "";

    /// <summary>The title of the dialog the action is in, or null on the page.</summary>
    public string? Dialog { get; set; }

    public List<CapturedEffect> Effects { get; } = [];

    /// <summary>A dialog the action opened that closed later with no action seen, which the flow closes after it.</summary>
    public List<CapturedEffect> Unseen { get; } = [];

    /// <summary>The checks the person chose right after the action, which the flow makes after it.</summary>
    public List<CapturedAction> Checks { get; } = [];

    /// <summary>
    /// For a checkbox or switch ticked or turned more than once in a row: whether it was on before the first time, which
    /// tells a round trip (on, then off again) that the flow needs none of.
    /// </summary>
    public bool? Before { get; set; }

    /// <summary>The choice's list is open already, as the steps before it opened it and typed into its filter.</summary>
    public bool Opened { get; set; }

    /// <summary>What was typed into an Autocomplete before an option was chosen.</summary>
    public string? Search { get; set; }

    public Setting? Value { get; set; }

    /// <summary>
    /// For a checkbox of a group of them (Sports, Reading, Music under "Hobbies"): its label as a value of the group, which
    /// a loop over the group's choices reads; outside a loop the checkbox is a flag of its own.
    /// </summary>
    public Setting? Member { get; set; }

    public Setting? SearchValue { get; set; }
    public Setting? OptionValue { get; set; }
    public Setting? ShownValue { get; set; }
    public Setting? RowKey { get; set; }

    /// <summary>The values the person typed, chose or checked for the action, in the order the flow uses them.</summary>
    public IEnumerable<Setting> Values => new[] { SearchValue, Value, OptionValue, ShownValue }.OfType<Setting>();

    /// <summary>The settings the action reads, each with its part in it, as the person's edits name them.</summary>
    public IEnumerable<(string Role, Setting Setting)> Parts =>
        new[] { ("search", SearchValue), ("value", Value), ("option", OptionValue), ("shown", ShownValue), ("row", RowKey) }
            .Where(part => part.Item2 is not null).Select(part => (part.Item1, part.Item2!));

    public bool Has(string effect) => Effects.Any(found => found.Kind == effect);
}

/// <summary>
/// A property of the flow's settings record, with the value recorded as its default: a text, a flag or a number, or
/// for a loop, the list of what each repeat typed. The person may give it another name or default, have the flow make
/// it unique per run, or type a date as days from today.
/// </summary>
internal sealed class Setting(string name, string? text, bool? flag = null)
{
    public string Name { get; set; } = name;

    /// <summary>The value recorded, which text on the page that shows it is told by.</summary>
    public string? Text { get; } = text;

    public bool? Flag { get; } = flag;

    /// <summary>The default the person gave it, else null for <see cref="Text"/>; for a relative date, the days.</summary>
    public string? Default { get; set; }

    /// <summary>
    /// What was typed exactly, when it is not <see cref="Text"/>: with the bidi marks the field held, which the text a
    /// page shows, and so <see cref="Text"/>, is compared without.
    /// </summary>
    public string? Exact { get; set; }

    /// <summary>For a secret, what was typed, which only a test run's values hold.</summary>
    public Secret? Hidden { get; init; }

    /// <summary>The default as the flow's YAML and record hold it.</summary>
    public string? Value => Default ?? Exact ?? Text;

    /// <summary>The person named it: other settings give way to it.</summary>
    public bool Named { get; set; }

    /// <summary>The flow types it with its run's stamp appended (<c>Unique(...)</c>), so it never repeats.</summary>
    public bool Unique { get; set; }

    /// <summary>The format a date or a time input takes its value in, as its record's doc comment says; null for text.</summary>
    public string? Format { get; set; }

    /// <summary>A date the flow types as <see cref="Value"/> days from the day it runs, in this format.</summary>
    public string? DateFormat { get; set; }

    /// <summary>
    /// A password or another secret: the flow's record has no default for it, and its YAML reads it from the environment
    /// (<c>${PASSWORD}</c>); only a test run in the panel is given the value recorded.
    /// </summary>
    public bool Secret { get; set; }

    /// <summary>
    /// <c>string</c>, <c>bool</c>, <c>int</c> or <c>decimal</c>; for a list of values, an array of one of them.
    /// </summary>
    public string Type { get; set; } = flag is null ? "string" : "bool";

    /// <summary>A name that tells it apart from another setting of the same name, e.g. <c>TicketToClose</c>.</summary>
    public string? Qualified { get; set; }

    /// <summary>For a list: each repeat's values, one for a list of values, a record's fields for a list of records.</summary>
    public IReadOnlyList<IReadOnlyList<Setting>>? Items { get; init; }

    /// <summary>For a list: the name and type of each of its items' values.</summary>
    public IReadOnlyList<Setting>? Fields { get; init; }

    /// <summary>For a list of records: the record's name, e.g. <c>Line</c>.</summary>
    public string? ItemType { get; set; }

    public bool IsNumber => Type is "int" or "decimal";

    /// <summary>The property's C# type.</summary>
    public string CSharpType => ItemType is { } item ? item + "[]" : Type;
}

/// <summary>A private method of the flow: actions on one page or in one dialog, ending where that changes.</summary>
internal sealed class Step
{
    public required string Page { get; init; }
    public string? Dialog { get; init; }
    public List<PlannedAction> Actions { get; } = [];
    public string Name { get; set; } = "";

    /// <summary>What the step does, in a sentence, for its doc comment.</summary>
    public string Summary { get; set; } = "";

    /// <summary>The dialog the step opens, which it returns.</summary>
    public CapturedDialog? Opens { get; set; }

    /// <summary>The page the step opens (a pop-up or a tab), which it returns.</summary>
    public string? OpensPage { get; set; }

    /// <summary>The step repeats its first <see cref="Loop.Length"/> actions, which the rest of its actions repeat.</summary>
    public Loop? Loop { get; set; }

    public bool Ended { get; set; }
}

/// <summary>
/// Actions the person did again and again, differing only in the values typed or chosen: the flow does them once for
/// each item of <see cref="List"/>, or <see cref="Count"/> times when they hold no value.
/// </summary>
internal sealed class Loop
{
    /// <summary>The actions of one repeat.</summary>
    public required int Length { get; init; }

    /// <summary>How many repeats were recorded.</summary>
    public required int Repeats { get; init; }

    public Setting? List { get; init; }

    /// <summary>The number of repeats, as an <c>int</c> setting, for repeats that hold no value.</summary>
    public Setting? Count { get; init; }

    /// <summary>The settings of each repeat that the list holds: what each typed or chose, the row it acted on.</summary>
    public HashSet<Setting> Holds { get; init; } = [];
}
