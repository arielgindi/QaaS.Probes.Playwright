using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Generation;
using QaaS.Playwright.Recorder.TestRun;

namespace QaaS.Playwright.Recorder.Panel;

/// <summary>
/// What the panel shows: the flow exactly as it will be saved, from the planner and the writer that save it, with
/// each step's actions, the lines each was written to, and every way the capture script found to locate its element.
/// </summary>
internal sealed record PanelView(
    string Flow, string Target, string Status, string Code, string Yaml, IReadOnlyList<PanelStep> Steps,
    IReadOnlyList<CheckByHand> Checks, string? Error)
{
    /// <summary>The flow's file is there already: Save keeps it, and saves the recording beside it.</summary>
    public bool Kept { get; init; }

    /// <summary>Why the recording is paused when the person did not pause it, e.g. the login flow failed.</summary>
    public string? Notice { get; init; }

    /// <summary>The login flow the recording runs after, by its class name.</summary>
    public string? Login { get; init; }

    /// <summary>How the login flow goes while it runs: "Logging in with LoginFlow… 12 s, on /signin", and what it did last.</summary>
    public PanelLoginProgress? LoginProgress { get; init; }

    /// <summary>The element the person picked in the Elements view, with every way to locate it.</summary>
    public PanelElement? Element { get; init; }

    /// <summary>The flow's test run going on, or the last one.</summary>
    public PanelTest? Test { get; init; }

    /// <summary>Whether the flow writes its auto checks: as the person turned them here, else as the command line says.</summary>
    public bool AutoChecks { get; init; } = true;

    /// <summary>The recorded page is in check mode: a click there picks an element to check.</summary>
    public bool Checking { get; init; }

    /// <summary>What Undo takes back, as the panel says it, e.g. "Restore the click on “Save”"; null when nothing.</summary>
    public string? Undo { get; init; }

    /// <summary>The recording this one continues, when it does.</summary>
    public PanelContinued? Continued { get; init; }

    /// <summary>The page reads it as JSON: camelCase, text of every script kept readable, nothing missing written.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

/// <summary>How the login flow goes while it runs, each second; slow, it has run long enough to offer logging in by hand.</summary>
internal sealed record PanelLoginProgress(string Text, string? Last, bool Slow);

/// <summary>
/// A recording that continues another: a divider with <paramref name="Label"/> after the last of that one's actions
/// still in the flow, and a banner while they replay, or once they did not.
/// </summary>
internal sealed record PanelContinued(string Label)
{
    /// <summary>The action the divider follows; null when none of the recording continued is left.</summary>
    public string? After { get; init; }

    public string? Banner { get; init; }
}

/// <summary>A method of the flow: the actions on one page or in one dialog.</summary>
internal sealed record PanelStep(string Name, string? Dialog, LineSpan? Lines, IReadOnlyList<PanelAction> Actions);

/// <summary>An action of the flow, as the panel's timeline and inspector show it.</summary>
internal sealed record PanelAction(string Id, string Kind)
{
    public string? Widget { get; init; }

    /// <summary>What the element is called on the page: its name, label or attribute.</summary>
    public string? Element { get; init; }

    /// <summary>What was typed, chosen, pressed, uploaded or opened.</summary>
    public string? Value { get; init; }

    /// <summary>The locator the flow uses, as a person reads it.</summary>
    public string? Locator { get; init; }

    public required string Page { get; init; }
    public string? Dialog { get; init; }
    public LineSpan? Lines { get; init; }
    public IReadOnlyList<PanelEffect> Effects { get; init; } = [];

    /// <summary>What to check by hand in the action's lines.</summary>
    public IReadOnlyList<string> Checks { get; init; } = [];

    /// <summary>The flow's settings the action reads, with their values as YAML.</summary>
    public IReadOnlyList<PanelSetting> Settings { get; init; } = [];

    public IReadOnlyList<PanelCandidate> Candidates { get; init; } = [];

    /// <summary>The candidate the person chose, when they did.</summary>
    public int? Chosen { get; init; }

    /// <summary>The candidate the flow uses.</summary>
    public int? Picked { get; init; }

    /// <summary>The candidate the policy would use.</summary>
    public int? Auto { get; init; }

    /// <summary>Added in the panel's Elements view rather than done in the app.</summary>
    public bool Added { get; init; }

    /// <summary>The checks after the action: its auto checks, those dropped too, and those the person chose.</summary>
    public IReadOnlyList<Expectation> Expects { get; init; } = [];
}

/// <summary>An element picked in the Elements view, located as a recorded action's element is.</summary>
internal sealed record PanelElement(int Node, string Page)
{
    /// <summary>What the element is called on the page, and its tag.</summary>
    public string? Name { get; init; }

    public string Tag { get; init; } = "";

    /// <summary>The locator the flow would use for it, as a person reads it.</summary>
    public string? Locator { get; init; }

    public string? Dialog { get; init; }
    public IReadOnlyList<PanelCandidate> Candidates { get; init; } = [];
    public int? Chosen { get; init; }
    public int? Picked { get; init; }
    public int? Auto { get; init; }

    /// <summary>It is not on the page now, e.g. an item of a menu that closed: its locators were found when frozen.</summary>
    public bool Gone { get; init; }

    /// <summary>A text field, which a fill can type into.</summary>
    public bool Fillable { get; init; }
}

/// <summary>A test run of the flow as the panel shows it: where it is, each action's state, and how it ended.</summary>
/// <param name="Run">The run's number, which changes with each run.</param>
/// <param name="State">
/// <c>building</c>, <c>window</c> (opening it), <c>start</c> (opening BaseUrl), <c>login</c>, <c>running</c>,
/// <c>passed</c>, <c>failed</c>, <c>stopped</c> or <c>error</c>.
/// </param>
/// <param name="Total">How many actions the run goes through.</param>
internal sealed record PanelTest(int Run, string State, int Total)
{
    /// <summary>The action "Run to here" runs to; null for the whole flow.</summary>
    public string? UpTo { get; init; }

    public bool SlowMotion { get; init; }

    /// <summary>The login flow that runs first.</summary>
    public string? Login { get; init; }

    /// <summary>When it started, in Unix milliseconds.</summary>
    public long Started { get; init; }

    /// <summary>When its state began, in Unix milliseconds, for the time of each phase before the first step.</summary>
    public long Since { get; init; }

    /// <summary>How long the browser's part took, once it ended.</summary>
    public long? Ms { get; init; }

    /// <summary>The lines of the statement running; in a loop, of its body's statement, which every repeat runs.</summary>
    public LineSpan? Running { get; init; }

    /// <summary>Each action's state, by its id.</summary>
    public IReadOnlyDictionary<string, PanelTestStep> Steps { get; init; } = new Dictionary<string, PanelTestStep>();

    public TestFailure? Failure { get; init; }

    /// <summary>Why it could not run, and more about it.</summary>
    public string? Problem { get; init; }

    public string? ProblemDetail { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>The flow changed since the run: its results are of the code as it was then.</summary>
    public bool Stale { get; init; }

    /// <summary>The report as the panel shows it, against the code it shows now.</summary>
    public static PanelTest Of(TestReport report, string code) => new(report.Run, StateOf(report), report.Total)
    {
        UpTo = report.UpTo, SlowMotion = report.SlowMotion, Login = report.Login, Started = report.Started, Since = report.Since,
        Ms = report.Ms,
        Running = report.Outcome is null ? report.Running : null,
        Steps = report.Steps.ToDictionary(pair => pair.Key, pair => new PanelTestStep(pair.Value.State switch
        {
            StepState.Running => "running",
            StepState.Passed => "passed",
            StepState.Failed => "failed",
            _ => "skipped",
        }) { Ms = pair.Value.Ms, Since = pair.Value.Since }),
        Failure = report.Failure, Problem = report.Problem, ProblemDetail = report.ProblemDetail, Warnings = report.Warnings,
        Stale = report.Code != code,
    };

    private static string StateOf(TestReport report) => report.Outcome switch
    {
        TestOutcome.Passed => "passed",
        TestOutcome.Failed => "failed",
        TestOutcome.Stopped => "stopped",
        TestOutcome.NotRun => "error",
        _ => report.Phase switch
        {
            TestPhase.Building => "building",
            TestPhase.Opening => "window",
            TestPhase.StartPage => "start",
            TestPhase.LoggingIn => "login",
            _ => "running",
        },
    };
}

/// <summary>An action's state in a test run: <c>running</c>, <c>passed</c>, <c>failed</c> or <c>skipped</c>.</summary>
internal sealed record PanelTestStep(string State)
{
    /// <summary>How long it took, in milliseconds.</summary>
    public long? Ms { get; init; }

    /// <summary>When it began, in Unix milliseconds, while it runs.</summary>
    public long? Since { get; init; }
}

/// <summary>What an action did to the page: a dialog opened, the address changed, an alert showed.</summary>
internal sealed record PanelEffect(string Kind, string? Text);

/// <summary>A setting an action reads, with its value as YAML, and how the person may change it.</summary>
internal sealed record PanelSetting(string Name, string Value)
{
    /// <summary>Its part in the action, by which an edit names it: <c>value</c>, <c>search</c>, <c>option</c>, <c>shown</c> or <c>row</c>.</summary>
    public string Role { get; init; } = "value";

    /// <summary>Its value as text, to edit; for a date relative to today, the days.</summary>
    public string? Text { get; init; }

    /// <summary><c>string</c>, <c>bool</c>, <c>int</c> or <c>decimal</c>.</summary>
    public string Type { get; init; } = "string";

    /// <summary>It may be renamed and typed another way; in a loop, only its value may change.</summary>
    public bool Renamable { get; init; }

    /// <summary>Whether the flow types it unique per run; null when it cannot, as it is not text typed.</summary>
    public bool? Unique { get; init; }

    /// <summary>The date relative to today the flow types it as.</summary>
    public PanelDate? Relative { get; init; }

    /// <summary>A date typed within a year of the day it was recorded: what typing it relative to today gives.</summary>
    public PanelDate? Suggest { get; init; }

    /// <summary>The value recorded, when the person gave it another.</summary>
    public string? Recorded { get; init; }
}

/// <summary>A date as days from today, in the format it was typed in, e.g. <c>dd/MM/yyyy</c> and 3.</summary>
internal sealed record PanelDate(string Format, int Days);

/// <summary>One way to find an element, and what it found when the action was recorded.</summary>
internal sealed record PanelCandidate(int Index, string Kind, string Label, string Code, int Count)
{
    /// <summary>Why it cannot be the locator, e.g. <c>found 2 (1 hidden)</c>; null when it can.</summary>
    public string? Rejection { get; init; }

    /// <summary>Whether it may be chosen at all: false for a generated id or a role Playwright does not have.</summary>
    public bool Usable { get; init; }

    /// <summary>Why it needs a look even so, e.g. Playwright's locator picks by position.</summary>
    public string? Problem { get; init; }
}

/// <summary>A view of the recording, and the actions in it its commands refer to.</summary>
internal sealed record PanelSnapshot(PanelView View, IReadOnlyDictionary<string, PlannedAction> Actions)
{
    /// <summary>The recorded actions that undoing the last action leaves out: it, and what led to it.</summary>
    public IReadOnlyList<string> Undo { get; init; } = [];

    /// <summary>
    /// The recorded actions that deleting an action of the flow leaves out, by its id: it, what only led to it, and the
    /// checks after it.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Sources { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>Plans and writes the recording as the recorder saves it, and describes it for the panel.</summary>
    /// <param name="recording">The recording so far.</param>
    /// <param name="request">The flow's name and folder.</param>
    /// <param name="namespaceName">The flow's namespace.</param>
    /// <param name="status"><c>recording</c>, <c>paused</c>, <c>login</c> or <c>saving</c>.</param>
    /// <param name="error">Why the last command did not work.</param>
    public static PanelSnapshot Of(Recording recording, RecordRequest request, string namespaceName, string status, string? error)
    {
        var plan = FlowPlanner.Plan(recording, request.ClassName);
        var flow = FlowWriter.Write(plan, namespaceName, request.AutoChecks);
        var steps = plan.Steps.Select(step => new PanelStep(step.Name, step.Dialog, flow.StepLines.GetValueOrDefault(step.Name),
            [.. step.Actions.Select(action => Describe(action, flow, inLoop: step.Loop is not null))])).ToList();
        var target = Path.Combine(request.OutputDir, $"{request.ClassName}.cs");
        var view = new PanelView(request.ClassName, target, status, flow.Code, flow.Yaml, steps, flow.Checks, error)
        {
            Kept = request.Replaces is null && File.Exists(target), Login = recording.After,
            AutoChecks = plan.AutoChecks ?? request.AutoChecks,
        };

        var planned = plan.Steps.SelectMany(step => step.Actions).ToList();
        return new PanelSnapshot(view, planned.DistinctBy(action => action.Source.Id).ToDictionary(action => action.Source.Id))
        {
            Undo = UndoOf(recording, planned), Sources = SourcesOf(recording, planned),
        };
    }

    // Each action of the flow, what only led to it since the action before it (opening a Select, typing into an
    // Autocomplete), and the checks after it.
    private static Dictionary<string, IReadOnlyList<string>> SourcesOf(Recording recording, List<PlannedAction> planned)
    {
        var ids = recording.Actions.Select(action => action.Id).ToList();
        var sources = new Dictionary<string, IReadOnlyList<string>>();
        for (var index = 0; index < planned.Count; index++)
        {
            var action = planned[index];
            var at = ids.IndexOf(action.Source.Id);
            var from = index > 0 ? ids.IndexOf(planned[index - 1].Source.Id) + 1 : 0;
            var led = at > from ? recording.Actions.Skip(from).Take(at - from).Where(other => other.Kind != "expect") : [];
            sources.TryAdd(action.Source.Id, [.. led.Select(other => other.Id), action.Source.Id, .. action.Checks.Select(check => check.Id)]);
        }

        return sources;
    }

    // The last action the flow does, and every recorded action after the one before it: what only led to it (opening
    // a Select, typing into an Autocomplete) goes with it. A check chosen last goes alone.
    private static List<string> UndoOf(Recording recording, List<PlannedAction> planned)
    {
        if (planned.Count == 0) return [];
        if (recording.Actions[^1] is { Kind: "expect" } check) return [check.Id];
        var ids = recording.Actions.Select(action => action.Id).ToList();
        var before = planned.Count > 1 ? ids.IndexOf(planned[^2].Source.Id) : -1;
        return planned.Count > 1 && before < 0 ? [planned[^1].Source.Id] : [.. ids.Skip(before + 1)];
    }

    private static PanelAction Describe(PlannedAction action, GeneratedFlow flow, bool inLoop)
    {
        var source = action.Source;
        var element = source.Element;
        var lines = flow.ActionLines.GetValueOrDefault(source.Id);
        var candidates = element?.Candidates ?? [];
        // The policy's own pick, as if the person had chosen none.
        var auto = LocatorPolicy.Choose(new CapturedTarget { Candidates = candidates }, byName: source.Widget == "radio");
        return new PanelAction(source.Id, source.Kind)
        {
            Widget = source.Widget,
            Element = element is null ? null : element.Text ?? element.Label ?? element.Name ?? element.Id ?? element.Tag,
            Value = ValueOf(source),
            Locator = LocatorPolicy.Describe(action.Locator),
            Page = source.Page,
            Dialog = action.Dialog,
            Lines = lines,
            Effects = [.. action.Effects.Select(EffectOf).OfType<PanelEffect>().Distinct()],
            Checks = [.. flow.Checks.Where(check => lines is not null && check.Line >= lines.First && check.Line <= lines.Last)
                .Select(check => check.Message)],
            Settings = [.. action.Parts.DistinctBy(part => part.Setting).Select(part => SettingOf(action, part.Role, part.Setting, inLoop))],
            Candidates = CandidatesOf(candidates),
            Chosen = element?.Chosen,
            Picked = IndexOf(candidates, action.Locator),
            Auto = IndexOf(candidates, auto),
            Added = source.Added,
            Expects = flow.Expects.GetValueOrDefault(source.Id) ?? [],
        };
    }

    // A setting, and how the person may change it: text typed may be unique per run, and a date typed within a year of
    // the day it was recorded may be typed as days from the day the flow runs.
    private static PanelSetting SettingOf(PlannedAction action, string role, Setting setting, bool inLoop)
    {
        var typed = role == "value" && action.Kind == "fill" && !inLoop && setting.Flag is null;
        return new PanelSetting(setting.Name, FlowYaml.Scalar(setting))
        {
            Role = role, Text = setting.Value, Type = setting.Type, Renamable = !inLoop,
            Unique = typed && setting.DateFormat is null ? setting.Unique : null,
            Relative = setting.DateFormat is { } format ? new PanelDate(format, int.Parse(setting.Value ?? "0", CultureInfo.InvariantCulture)) : null,
            Suggest = typed && setting.DateFormat is null && !setting.Unique
                      && RelativeDate.Of(setting.Text ?? "", RelativeDate.DayOf(action.Source.T)) is { } date
                ? new PanelDate(date.Format, date.Days)
                : null,
            Recorded = setting.Default is not null && setting.DateFormat is null && setting.Default != setting.Text ? setting.Text : null,
        };
    }

    /// <summary>
    /// An element picked in the Elements view, as a click on it would locate it: by the person's choice among its
    /// locators, else by the policy's.
    /// </summary>
    public static PanelElement Element(int node, string page, ElementDescription description, bool gone, bool fillable)
    {
        var target = description.Target ?? new CapturedTarget();
        var candidates = target.Candidates;
        var picked = LocatorPolicy.Choose(target);
        var auto = LocatorPolicy.Choose(new CapturedTarget { Candidates = candidates });
        return new PanelElement(node, page)
        {
            Name = target.Text ?? target.Label ?? target.Name ?? target.Id,
            Tag = target.Tag,
            Locator = LocatorPolicy.Describe(picked),
            Dialog = description.Dialog?.Title,
            Candidates = CandidatesOf(candidates),
            Chosen = target.Chosen,
            Picked = IndexOf(candidates, picked),
            Auto = IndexOf(candidates, auto),
            Gone = gone,
            Fillable = fillable,
        };
    }

    private static List<PanelCandidate> CandidatesOf(List<CapturedCandidate> candidates) =>
    [
        .. candidates.Select((candidate, index) => new PanelCandidate(index, candidate.Kind,
            LocatorPolicy.Describe(candidate) ?? candidate.Kind, Code(candidate), candidate.Count)
        {
            Rejection = LocatorPolicy.WhyLost(candidate),
            Usable = LocatorPolicy.CanUse(candidate),
            Problem = candidate.Kind == "generator" ? LocatorPolicy.Problem(candidate) : null,
        }),
    ];

    private static string? ValueOf(CapturedAction source) => source.Kind switch
    {
        "fill" or "choose" => source.Value,
        "press" => source.Key,
        "upload" => string.Join(", ", source.Files ?? []),
        "navigate" or "newTab" => Route(source.Url) ?? source.Url,
        "drag" => source.To?.Text,
        _ => null,
    };

    private static PanelEffect? EffectOf(CapturedEffect effect) => effect.Kind switch
    {
        "dialogOpened" or "dialogClosed" => new PanelEffect(effect.Kind, effect.Dialog?.Title),
        "urlChanged" => new PanelEffect(effect.Kind, Route(effect.Url) ?? effect.Url),
        "alert" or "jsDialog" => new PanelEffect(effect.Kind, effect.Text),
        "tabSelected" => new PanelEffect(effect.Kind, null),
        "popup" => new PanelEffect(effect.Kind, effect.Page),
        _ => null,
    };

    // The address as the flow checks it: its path, with a record's id in it as [id], as any id matches there.
    private static string? Route(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var route = uri.Fragment.StartsWith("#/", StringComparison.Ordinal) ? uri.AbsolutePath + uri.Fragment : uri.AbsolutePath;
        return string.Join('/', route.Split('/').Select(segment =>
            segment.Any(char.IsDigit) || (segment.Length > 12 && UnstableId.IsGuidLike(segment))
                ? "[id]"
                : Uri.UnescapeDataString(segment)));
    }

    private static string Code(CapturedCandidate candidate) => candidate.Kind == "generator"
        ? LocatorPolicy.RenderGenerated("page", candidate)
        : LocatorPolicy.Render("page", candidate.Parts);

    private static int? IndexOf(List<CapturedCandidate> candidates, CapturedCandidate? candidate) =>
        candidate is not null && candidates.IndexOf(candidate) is var index and >= 0 ? index : null;
}
