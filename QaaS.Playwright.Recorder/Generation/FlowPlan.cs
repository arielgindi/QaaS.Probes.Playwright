using QaaS.Playwright.Recorder.Capture;

namespace QaaS.Playwright.Recorder.Generation;

/// <summary>A recording turned into what the flow does: its steps, and the settings its values come from.</summary>
internal sealed class FlowPlan
{
    public required string ClassName { get; init; }
    public required string StartUrl { get; init; }

    /// <summary>The flow the recording started after, which a probe must run before it.</summary>
    public string? After { get; init; }
    public List<Step> Steps { get; } = [];

    /// <summary>The settings, in the order of the steps that read them.</summary>
    public List<Setting> Settings { get; } = [];

    /// <summary>What could not be recorded at all, for the person to do by hand.</summary>
    public List<string> NotRecorded { get; } = [];
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

    /// <summary>What was typed into an Autocomplete before an option was chosen.</summary>
    public string? Search { get; set; }

    public Setting? Value { get; set; }
    public Setting? SearchValue { get; set; }
    public Setting? OptionValue { get; set; }
    public Setting? ShownValue { get; set; }
    public Setting? RowKey { get; set; }

    /// <summary>The values the person typed, chose or checked for the action, in the order the flow uses them.</summary>
    public IEnumerable<Setting> Values => new[] { SearchValue, Value, OptionValue, ShownValue }.OfType<Setting>();

    public bool Has(string effect) => Effects.Any(found => found.Kind == effect);
}

/// <summary>
/// A property of the flow's settings record, with the value recorded as its default: a text, a flag or a number, or
/// for a loop, the list of what each repeat typed.
/// </summary>
internal sealed class Setting(string name, string? text, bool? flag = null)
{
    public string Name { get; set; } = name;
    public string? Text { get; } = text;
    public bool? Flag { get; } = flag;

    /// <summary>
    /// <c>string</c>, <c>bool</c>, <c>int</c> or <c>decimal</c>; for a list of values, an array of one of them.
    /// </summary>
    public string Type { get; init; } = flag is null ? "string" : "bool";

    /// <summary>A name that tells it apart from another setting of the same name, e.g. <c>TicketToClose</c>.</summary>
    public string? Qualified { get; init; }

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
}
