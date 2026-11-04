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

    /// <summary>A part of the flow the person records again, or records after, and how it goes.</summary>
    public PanelRerecord? Rerecord { get; init; }

    /// <summary>While trying (F7): each action tried, as the recorder would write it, oldest first.</summary>
    public IReadOnlyList<PanelAction>? Tried { get; init; }

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

/// <summary>
/// A part of the flow the person records again, or records after (<paramref name="After"/>), named
/// <paramref name="Part"/>: <c>replaying</c> the flow up to it, <c>failed</c> to, <c>recording</c> it, or <c>done</c>.
/// </summary>
internal sealed record PanelRerecord(string Part, string Phase, bool After)
{
    /// <summary>The part is a loop, recorded again whole.</summary>
    public bool Loop { get; init; }

    /// <summary>The actions of the flow recorded in the part so far.</summary>
    public IReadOnlyList<string> Fresh { get; init; } = [];

    /// <summary>The action of the flow what is recorded follows, while nothing is yet; null at its start.</summary>
    public string? At { get; init; }

    /// <summary>The action the replay failed at; null when it failed before the first, e.g. in the login flow.</summary>
    public string? FailedAt { get; init; }

    /// <summary>Why the replay could not run at all.</summary>
    public string? Problem { get; init; }

    /// <summary>Once done: the recording is paused, as the app is not where the flow ends.</summary>
    public bool Paused { get; init; }
}

/// <summary>
/// A part of the flow to record again, or to record after, named <paramref name="Label"/>: the recorded actions it
/// replaces, the recorded actions what is recorded goes after and before (null: at the start, at the end), the action
/// of the flow a replay runs to first (null: none), and the part's first and last actions of the flow.
/// </summary>
internal sealed record RerecordPart(string Label, bool Loop)
{
    public IReadOnlyList<string> Replaced { get; init; } = [];
    public string? After { get; init; }
    public string? Before { get; init; }
    public string? UpTo { get; init; }
    public string First { get; init; } = "";
    public string Last { get; init; } = "";
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

    /// <summary>Tried (F7), not recorded: a card of what the recorder would write for it.</summary>
    public bool Tried { get; init; }

    /// <summary>For an action tried: why each locator tried before the one chosen lost, as the overlay says it.</summary>
    public IReadOnlyList<string>? Reasons { get; init; }

    /// <summary>For an action tried: what the overlay notes, e.g. that the element is no control.</summary>
    public string? Note { get; init; }

    /// <summary>For an action tried that the flow would leave out: why.</summary>
    public string? Omitted { get; init; }

    /// <summary>What the recorder suggests leaving out of the flow around the action, one click each.</summary>
    public IReadOnlyList<PanelSuggestion> Suggestions { get; init; } = [];
}

/// <summary>A suggestion, as the panel shows it on the cards of the actions it is about: what it says, and its button.</summary>
internal sealed record PanelSuggestion(string Key, string Text, string Apply);

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

    /// <summary>
    /// The lines each step on it would write, before it is made: <c>use</c>, the selected step acting on it instead, and
    /// <c>click</c>, <c>fill</c> and <c>check</c>, added where the next action goes.
    /// </summary>
    public IReadOnlyList<PanelPreview> Previews { get; init; } = [];

    /// <summary>The checks that fit it, the likeliest at <see cref="Check"/>.</summary>
    public IReadOnlyList<PanelCheck> Checks { get; init; } = [];

    public int Check { get; init; }

    /// <summary>
    /// What Enter makes of it: <c>use</c> when it is around, inside or in the same row as the selected step's element,
    /// else <c>fill</c> for a text field and <c>click</c>.
    /// </summary>
    public string? Primary { get; init; }
}

/// <summary>The lines a step on the picked element would write; for <c>use</c>, the step's lines as they are now too.</summary>
internal sealed record PanelPreview(string Kind, string Code)
{
    public string? Was { get; init; }
}

/// <summary>A check that fits the picked element: what it expects (<c>text</c>, <c>visible</c>, ...), and of what value.</summary>
internal sealed record PanelCheck(string Kind, string Name)
{
    public string? Value { get; init; }
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
    /// <summary>The recording the view is of.</summary>
    public Recording Recording { get; init; } = new("", []);

    /// <summary>The steps of the flow, as planned.</summary>
    public IReadOnlyList<Step> Steps { get; init; } = [];

    /// <summary>The recorded actions that undoing the last action leaves out: it, and what led to it.</summary>
    public IReadOnlyList<string> Undo { get; init; } = [];

    /// <summary>
    /// The recorded actions that deleting an action of the flow leaves out, by its id: it, what only led to it, and the
    /// checks after it.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Sources { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>What the recorder suggests leaving out, each with every recorded action applying it leaves out.</summary>
    public IReadOnlyList<(Suggestion Suggestion, IReadOnlyList<string> Drop)> Suggestions { get; init; } = [];

    /// <summary>Plans and writes the recording as the recorder saves it, and describes it for the panel.</summary>
    /// <param name="recording">The recording so far.</param>
    /// <param name="request">The flow's name and folder.</param>
    /// <param name="namespaceName">The flow's namespace.</param>
    /// <param name="status"><c>recording</c>, <c>paused</c>, <c>login</c> or <c>saving</c>.</param>
    /// <param name="error">Why the last command did not work.</param>
    /// <param name="fresh">The actions recorded in the part being recorded again, which Undo takes back first.</param>
    public static PanelSnapshot Of(Recording recording, RecordRequest request, string namespaceName, string status, string? error,
        IReadOnlySet<string>? fresh = null)
    {
        // An action the recorder cannot plan or write is left out and listed, never the whole panel.
        var (plan, flow) = FlowCodeGenerator.PlanAndWrite(recording, request.ClassName, namespaceName, request.AutoChecks);
        var steps = plan.Steps.Select(step => new PanelStep(step.Name, step.Dialog, flow.StepLines.GetValueOrDefault(step.Name),
            [.. step.Actions.Select(action => Describe(action, flow, inLoop: step.Loop is not null) with
            {
                Suggestions = [.. plan.Suggestions.Where(suggestion => suggestion.On.Contains(action.Source.Id))
                    .Select(suggestion => new PanelSuggestion(suggestion.Key, suggestion.Text, suggestion.Apply))],
            })])).ToList();
        var target = Path.Combine(request.OutputDir, $"{request.ClassName}.cs");
        var view = new PanelView(request.ClassName, target, status, flow.Code, flow.Yaml, steps, flow.Checks, error)
        {
            Kept = request.Replaces is null && File.Exists(target), Login = recording.After,
            AutoChecks = plan.AutoChecks ?? request.AutoChecks,
        };

        var planned = plan.Steps.SelectMany(step => step.Actions).ToList();
        var sources = SourcesOf(recording, planned);
        return new PanelSnapshot(view, planned.DistinctBy(action => action.Source.Id).ToDictionary(action => action.Source.Id))
        {
            Undo = fresh is { Count: > 0 } ? FreshUndo(recording, planned, fresh, sources) : UndoOf(recording, planned),
            Sources = sources, Recording = recording, Steps = plan.Steps,
            // Applying one leaves out what only led to its actions too, as deleting them does.
            Suggestions = [.. plan.Suggestions.Select(suggestion => (suggestion,
                (IReadOnlyList<string>)[.. suggestion.Drop.SelectMany(id => sources.GetValueOrDefault(id) ?? [id]).Distinct()]))],
        };
    }

    /// <summary>
    /// The part of the flow the action <paramref name="actionId"/> is, or its whole step: the recorded actions it is
    /// made of, which recording it again replaces, where what is recorded then goes, and the action of the flow the
    /// replay runs to first (null for none). <paramref name="after"/> records after it instead, replacing nothing: the
    /// replay runs through it. A loop is a part whole, named for its step; else the part is called
    /// <paramref name="label"/>, as the person sees it. Null when the flow has no such action.
    /// </summary>
    public RerecordPart? PartOf(string actionId, bool wholeStep, bool after, string label)
    {
        var step = Steps.FirstOrDefault(step => step.Actions.Any(action => action.Source.Id == actionId));
        if (step is null) return null;
        var loop = step.Loop is not null;
        var part = (wholeStep || loop ? step.Actions.Select(action => action.Source.Id) : [actionId]).ToHashSet();
        return PartBetween(part, after, loop ? step.Name : label, loop);
    }

    /// <summary>
    /// The part from the action <paramref name="from"/> to the end of <paramref name="part"/>: recorded again from
    /// where a replay up to <paramref name="part"/> stopped.
    /// </summary>
    public RerecordPart? PartFrom(string from, RerecordPart part, string label)
    {
        var order = Planned();
        var first = order.IndexOf(Steps.FirstOrDefault(step => step.Loop is not null && step.Actions.Any(action => action.Source.Id == from))
            ?.Actions[0].Source.Id ?? from);
        var last = Math.Max(order.IndexOf(part.Last), first);
        return first < 0 ? null : PartBetween([.. order.Skip(first).Take(last - first + 1)], after: false, label, loop: false);
    }

    // The flow's actions, in the order it does them; a loop's repeat by repeat.
    private List<string> Planned() => [.. Steps.SelectMany(step => step.Actions).Select(action => action.Source.Id).Distinct()];

    private RerecordPart? PartBetween(HashSet<string> part, bool after, string label, bool loop)
    {
        var order = Planned();
        var first = order.FindIndex(part.Contains);
        var last = order.FindLastIndex(part.Contains);
        if (first < 0) return null;
        var ids = Recording.Actions.Select(action => action.Id).ToList();
        var next = last + 1 < order.Count ? order[last + 1] : null;
        // From what led to the part's first action to what led to the action after it, but what belongs to others.
        var start = ids.IndexOf(Sources[order[first]][0]);
        var end = next is null ? ids.Count - 1 : ids.IndexOf(Sources[next][0]) - 1;
        var others = order.Where(id => !part.Contains(id)).SelectMany(id => Sources[id]).ToHashSet();
        return new RerecordPart(label, loop)
        {
            Replaced = after ? [] : [.. ids.Skip(start).Take(end - start + 1).Where(id => !others.Contains(id))],
            After = after ? ids[end] : start > 0 ? ids[start - 1] : null,
            Before = next is null ? null : ids[end + 1],
            UpTo = after ? order[last] : first > 0 ? order[first - 1] : null,
            First = order[first],
            Last = order[last],
        };
    }

    // In the part being recorded again, the last of the flow's actions recorded in it, with what led to it and all
    // recorded after it; a check chosen last goes alone.
    private static List<string> FreshUndo(Recording recording, List<PlannedAction> planned, IReadOnlySet<string> fresh,
        IReadOnlyDictionary<string, IReadOnlyList<string>> sources)
    {
        var ids = recording.Actions.Where(action => fresh.Contains(action.Id)).Select(action => action.Id).ToList();
        if (recording.Actions.LastOrDefault(action => fresh.Contains(action.Id)) is { Kind: "expect" } check) return [check.Id];
        var last = planned.LastOrDefault(action => fresh.Contains(action.Source.Id));
        if (last is null) return ids;
        var from = ids.IndexOf(sources[last.Source.Id][0]);
        return [.. ids.Skip(Math.Max(from, 0))];
    }

    private static Dictionary<string, IReadOnlyList<string>> SourcesOf(Recording recording, List<PlannedAction> planned) =>
        FlowPlanner.Sources(recording, planned);

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
            Element = element is null ? null : Names.Human(element) ?? element.Name ?? element.Id ?? element.Tag,
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

    /// <summary>
    /// What the recorder would write for each action the person tried (F7), had it been recorded where the next action
    /// goes: before the action <paramref name="before"/>, else last. Each is planned and written in the flow alone, as
    /// it would be: its locator and why the ones before it lost, the auto checks after it, and the setting a value it
    /// typed or chose would be; or why the flow would leave it out. With each, the planned action, to point at it.
    /// </summary>
    public static IReadOnlyList<(PanelAction Card, PlannedAction? Planned)> Tried(Recording recording, Recording tried,
        string? before, RecordRequest request, string namespaceName)
    {
        var at = before is null ? -1 : recording.Actions.ToList().FindIndex(action => action.Id == before);
        var cards = new List<(PanelAction, PlannedAction?)>();
        foreach (var action in tried.Actions.Where(action => action.Kind != "expect"))
        {
            List<CapturedAction> actions = [.. recording.Actions];
            actions.Insert(at < 0 ? actions.Count : at, action);
            var (plan, flow) = FlowCodeGenerator.PlanAndWrite(recording with { Actions = actions }, request.ClassName, namespaceName,
                request.AutoChecks);
            var step = plan.Steps.FirstOrDefault(step => step.Actions.Any(planned => planned.Source.Id == action.Id));
            var planned = step?.Actions.First(planned => planned.Source.Id == action.Id);
            var card = planned is null
                ? Unplanned(action)
                : Describe(planned, flow, inLoop: step!.Loop is not null) with { Lines = null };
            var element = action.Element;
            cards.Add((card with
            {
                Tried = true,
                Reasons = element is null ? [] : LocatorPolicy.WhyNot(element, planned?.Locator, byName: action.Widget == "radio"),
                Note = element is { Interactive: false } ? "not a control: kept only if the page reacts" : null,
                Omitted = planned is null ? WhyLeftOut(action) : null,
            }, planned));
        }

        return cards;
    }

    // An action tried that the flow leaves out, as the recorder saw it.
    private static PanelAction Unplanned(CapturedAction action)
    {
        var element = action.Element;
        var candidates = element?.Candidates ?? [];
        var chosen = LocatorPolicy.Choose(element, byName: action.Widget == "radio");
        return new PanelAction(action.Id, action.Kind)
        {
            Widget = action.Widget, Page = action.Page, Value = ValueOf(action),
            Element = element is null ? null : Names.Human(element) ?? element.Name ?? element.Id ?? element.Tag,
            Locator = LocatorPolicy.Describe(chosen), Candidates = CandidatesOf(candidates), Picked = IndexOf(candidates, chosen),
            Auto = IndexOf(candidates, chosen), Effects = [.. action.Effects.Select(EffectOf).OfType<PanelEffect>().Distinct()],
        };
    }

    private static string WhyLeftOut(CapturedAction action) => action switch
    {
        { Widget: "select-open" or "autocomplete-button" } => "It opens the list: the flow chooses the option picked next, and opens it then.",
        { Kind: "press", Key: "Escape" } => "Escape changed nothing on the page, so the flow leaves it out.",
        { Kind: "click" or "dblclick" } => "A click on no control that changed nothing: the flow leaves it out.",
        _ => "It only leads to the next action: the flow does it as part of that one.",
    };

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
                      && RelativeDate.Of(setting.Text ?? "", RelativeDate.DayOf(action.Source.T), action.Source.Element?.Placeholder) is { } date
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
