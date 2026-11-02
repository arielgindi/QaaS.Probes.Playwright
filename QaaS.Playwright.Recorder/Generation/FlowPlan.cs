using QaaS.Playwright.Recorder.Capture;

namespace QaaS.Playwright.Recorder.Generation;

/// <summary>A recording turned into what the flow does: its steps, and the settings its values come from.</summary>
internal sealed class FlowPlan
{
    public required string ClassName { get; init; }
    public required string StartUrl { get; init; }
    public List<Step> Steps { get; } = [];
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

    public bool Has(string effect) => Effects.Any(found => found.Kind == effect);
}

/// <summary>A property of the flow's settings record, with the value recorded as its default.</summary>
internal sealed record Setting(string Name, string? Text, bool? Flag)
{
    public string Type => Flag is null ? "string" : "bool";
}

/// <summary>A private method of the flow: actions on one page or in one dialog, ending where that changes.</summary>
internal sealed class Step
{
    public required string Page { get; init; }
    public string? Dialog { get; init; }
    public List<PlannedAction> Actions { get; } = [];
    public string Name { get; set; } = "";

    /// <summary>The dialog the step opens, which it returns.</summary>
    public CapturedDialog? Opens { get; set; }

    /// <summary>The page the step opens (a pop-up or a tab), which it returns.</summary>
    public string? OpensPage { get; set; }

    public bool Ended { get; set; }
}
