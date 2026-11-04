using System.Text;
using System.Text.RegularExpressions;
using QaaS.Playwright.Recorder.Capture;

namespace QaaS.Playwright.Recorder.Generation;

/// <summary>A line of the flow a person should look at, and why.</summary>
internal sealed record CheckByHand(int? Line, string Message);

/// <summary>Lines of a flow's code, numbered from 1, both included.</summary>
internal sealed record LineSpan(int First, int Last);

/// <summary>
/// A statement of a step written for recorded actions: its lines, and the actions it does, in the order it does them:
/// one, or in a loop's body the action of each repeat, the first repeat's first.
/// </summary>
internal sealed record ActionStatement(LineSpan Lines, IReadOnlyList<string> Actions);

/// <summary>
/// A check of the flow after an action: an auto check, by its key (<c>alert:...</c>, <c>url</c>, ...), or one the
/// person chose, by its id. <see cref="Text"/> says what it expects, <see cref="Of"/> of what; a dropped auto check is
/// not written.
/// </summary>
internal sealed record Expectation(string Key, string Text, bool Auto)
{
    public string? Of { get; init; }
    public bool Dropped { get; init; }
}

/// <summary>
/// The flow's source, its settings as YAML, and what to check by hand. The YAML holds no secret: each is the placeholder
/// of an environment variable (<c>${PASSWORD}</c>).
/// </summary>
internal sealed record GeneratedFlow(string Code, string Yaml, IReadOnlyList<CheckByHand> Checks)
{
    /// <summary>The settings as YAML with the secrets recorded, which only a test run in the panel reads.</summary>
    public string Values { get; init; } = Yaml;

    /// <summary>The settings that hold a secret, which the YAML reads from the environment.</summary>
    public IReadOnlyList<Setting> Secrets { get; init; } = [];

    /// <summary>What the recorder suggested leaving out, which the person did not apply in the panel.</summary>
    public IReadOnlyList<Suggestion> Suggestions { get; init; } = [];

    /// <summary>
    /// The lines written for each recorded action, by the action's id. An action a loop repeats has the lines of the
    /// loop's body that do it, as every repeat of it has.
    /// </summary>
    public IReadOnlyDictionary<string, LineSpan> ActionLines { get; init; } = new Dictionary<string, LineSpan>();

    /// <summary>The lines of each step's method, by the method's name.</summary>
    public IReadOnlyDictionary<string, LineSpan> StepLines { get; init; } = new Dictionary<string, LineSpan>();

    /// <summary>Each statement written for recorded actions, in the order of the code.</summary>
    public IReadOnlyList<ActionStatement> Statements { get; init; } = [];

    /// <summary>
    /// The checks after each action, by its id: the auto checks, dropped ones too, and those the person chose. An action
    /// a loop repeats has the checks of the loop's body.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Expectation>> Expects { get; init; } =
        new Dictionary<string, IReadOnlyList<Expectation>>();
}

/// <summary>
/// Writes a <see cref="FlowPlan"/> as a flow class: <c>RunAsync</c> calls one method per step, each step acts on the
/// page or dialog it is given and ends with the Expect of what it changed, and every recorded value is read from the
/// flow's settings. A locator used twice is a local of its own; a loop step does its first repeat for each item of its
/// list. Each Expect the recorder adds as a check is marked <c>// auto check</c>, and left out without auto checks or
/// when the person dropped it; a wait for the page to stop loading stays. A check the person chose is marked
/// <c>// check</c> and always written. A value typed before is read from its setting wherever a later locator, text or
/// check shows it.
/// </summary>
internal sealed partial class FlowWriter
{
    /// <summary>The longest line of the flow's code, as in this repository's own; a mark of an auto check may pass it.</summary>
    public const int MaxWidth = 120;

    // Where a statement may break onto the next line, indented once more.
    private const string Break = "\u001F";
    /// <summary>What ends the line of a check the recorder added itself.</summary>
    public const string AutoCheck = " // auto check";

    /// <summary>What ends the line of a check the person chose.</summary>
    public const string ChosenCheck = " // check";
    private const string SlowTimeout = "new() { Timeout = 30_000 }";

    // A click through what covers a checkbox: the box a library draws over it, inside its label.
    private const string ForceOptions = "new() { Force = true }";

    private static bool Covered(CapturedAction source) => source.Target?.Covered == true;

    private const string FileToUploadMethod = """
            // The recorder saw only the names of the files you uploaded: return each one's full path.
            private static string FileToUpload(string recordedName) =>
                throw new NotImplementedException($"Return the full path of the file recorded as '{recordedName}'.");
        """;

    // A setting read into a CSS selector's quotes is escaped there as the selector reads it, whatever it holds.
    private const string CssTextMethod = """
            // A value as the quotes of a CSS selector hold it, whatever it holds.
            private static string CssText(string value) =>
                value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)
                    .Replace("\n", "\\a ", StringComparison.Ordinal).Replace("\r", "\\d ", StringComparison.Ordinal);
        """;

    private readonly FlowPlan _plan;
    private readonly bool _autoChecks;
    private readonly List<Line> _run = [];
    private readonly List<(Step Step, string Signature, List<Line> Body)> _methods = [];
    private readonly Dictionary<string, string> _dialogVariables = [];
    private readonly Symbols _runSymbols = new(["page"]);
    private readonly Dictionary<string, HashSet<string>> _dropped = [];
    private readonly Dictionary<string, List<Expectation>> _expects = [];
    private readonly HashSet<Setting> _settings;

    // The method that accepts a browser dialog, and the pages that have it as their handler so far.
    private const string AcceptBrowserDialog = "AcceptBrowserDialog";
    private HashSet<string> _acceptsDialogs = [];

    // The settings read so far, in order; those typed, which a later locator may show. Each step is written twice from
    // the same ones.
    private List<Setting> _seen = [];
    private List<Setting> _typed = [];

    // What a loop's last round typed, as later steps read it: from the list's last item, by the text it is.
    private readonly Dictionary<string, string> _afterLoops = new(StringComparer.Ordinal);

    // The flow's properties that type a date relative to the day it runs, by the setting of its days.
    private readonly Dictionary<Setting, string> _dates = [];

    // The step being written; each step is written twice, first to count how often each locator is used.
    private StepState _step = null!;

    private FlowWriter(FlowPlan plan, bool autoChecks)
    {
        (_plan, _autoChecks, _settings) = (plan, plan.AutoChecks ?? autoChecks, [.. plan.Settings]);
        foreach (var action in plan.Steps.SelectMany(step => step.Actions))
            if (action.Source.Edit?.Dropped is { Count: > 0 } dropped) _dropped[action.Source.Id] = [.. dropped];
    }

    /// <param name="plan">The flow to write.</param>
    /// <param name="namespaceName">The flow's namespace.</param>
    /// <param name="autoChecks">
    /// Whether to check after each action what it did when recorded, unless the person turned it in the panel; waits stay
    /// without.
    /// </param>
    public static GeneratedFlow Write(FlowPlan plan, string namespaceName, bool autoChecks = true)
    {
        var writer = new FlowWriter(plan, autoChecks);
        foreach (var step in plan.Steps) writer.WriteStep(step);
        return writer.Assemble(namespaceName);
    }

    /// <summary>A statement of the flow, maybe with <see cref="Break"/>s; <see cref="Depth"/> 1 is in a loop.</summary>
    private sealed record Line(string Text, string? Check = null)
    {
        /// <summary>The ids of the recorded actions the line is written for: one, or each repeat of it in a loop.</summary>
        public IReadOnlyList<string> Actions { get; init; } = [];

        /// <summary>What ends its last line: the mark of a check, auto or chosen.</summary>
        public string Mark { get; init; } = "";

        public int Depth { get; init; }
    }

    // Where a step's actions run: its dialog, whose page is dialog.Page, or a page; Alias is the page's own name.
    private sealed record Scope(string Root, string Page, string? Dialog, string Alias);

    private sealed class StepState(Scope scope, StepState? counted)
    {
        public Scope Scope { get; } = scope;

        /// <summary>How often each locator is used, as counted by the first writing; null while counting.</summary>
        public Dictionary<string, int>? Uses { get; } = counted?.Counted;

        /// <summary>The locators found in each row, as counted by the first writing; null while counting.</summary>
        public Dictionary<string, HashSet<string>>? RowUses { get; } = counted?.RowsCounted;

        public Dictionary<string, int> Counted { get; } = [];
        public Dictionary<string, HashSet<string>> RowsCounted { get; } = [];
        /// <summary>The step's names: its parameter, and <c>page</c>, which a dialog's step has not but must not declare.</summary>
        public Symbols Symbols { get; } = new([scope.Root], ["page"]);
        public Dictionary<string, string> Declared { get; } = [];
        public List<Line> Lines { get; } = [];

        /// <summary>Lines to write before the loop: the locals its body uses that its items do not change.</summary>
        public List<Line> BeforeLoop { get; } = [];

        /// <summary>The C# of each value a loop's item holds, e.g. <c>line.Quantity</c>, by the first repeat's setting.</summary>
        public Dictionary<Setting, string> LoopValues { get; } = [];

        /// <summary>The local of each dialog a loop's round opens, by its title.</summary>
        public Dictionary<string, string> LoopDialogs { get; } = [];

        public IReadOnlyList<string> Actions { get; set; } = [];

        /// <summary>In a loop: each repeat's actions, and the place in them of the action being written.</summary>
        public (List<PlannedAction[]> All, int Index)? Repeats { get; set; }

        public int Depth { get; set; }
        public bool UsesFlow { get; set; }

        /// <summary>The dialog the step's last action opened, once its variable is declared, which checks after it use.</summary>
        public (string Title, string Variable)? Opened { get; set; }

        /// <summary>The C# written since it was reset reads a loop's item.</summary>
        public bool ReadsItem { get; set; }

        public void Add(string text, string? check = null, string mark = "") =>
            Lines.Add(new Line(text, check) { Actions = Actions, Mark = mark, Depth = Depth });
    }

    private void WriteStep(Step step)
    {
        var scope = step.Dialog is not null
            ? new Scope("dialog", "dialog.Page", step.Dialog, step.Page)
            : new Scope(step.Page, step.Page, null, step.Page);

        var (seen, typed, accepts) = (_seen.ToList(), _typed.ToList(), _acceptsDialogs.ToHashSet());
        _step = new StepState(scope, counted: null);
        WriteBody(step, last: false);
        (_seen, _typed, _acceptsDialogs) = (seen, typed, accepts);
        _step = new StepState(scope, _step);
        WriteBody(step, last: true);
        var body = _step;
        // A step that writes nothing, of actions the recorder does not know, is no method: they are to redo by hand.
        if (body.Lines.Count == 0) return;

        // A page or a dialog no step before opened is found in RunAsync, as the step before it: from what was read
        // before it, written in RunAsync's own state, not the step before's, whose loop item RunAsync has not.
        (var seenAfter, var typedAfter, _seen, _typed) = (_seen, _typed, seen, typed);
        _step = new StepState(new Scope("page", "page", null, "page"), counted: null);
        var argument = PageVariable(step);
        if (step.Dialog is { } title) argument = DialogVariable(step, title);
        (_seen, _typed) = (seenAfter, typedAfter);

        var returns = step.Opens is not null ? "Task<ILocator>" : step.OpensPage is not null ? "Task<IPage>" : "Task";
        var parameter = step.Dialog is not null ? "ILocator dialog" : $"IPage {step.Page}";
        _methods.Add((step, $"private {(body.UsesFlow ? "" : "static ")}async {returns} {step.Name}({parameter})", body.Lines));

        // What a step opens is kept when a later step works in it; a tab of a name opened before is the one opened now.
        var call = $"{step.Name}({argument})";
        var later = _plan.Steps.Skip(_plan.Steps.IndexOf(step) + 1).ToList();
        if (step.Opens is { } opened && later.Any(other => other.Dialog == opened.Title))
        {
            var variable = NewDialogVariable(step);
            _dialogVariables[opened.Title] = variable;
            _run.Add(new Line($"var {variable} = await {call};"));
        }
        else if (step.OpensPage is { } page && later.Any(other => other.Page == page))
            _run.Add(new Line($"{_runSymbols.Assign(page)} = await {call};"));
        else
            _run.Add(new Line($"await {call};"));
    }

    // A page no step before opened, as a pop-up whose opening click was left out, is opened in RunAsync: in a new tab, at
    // the address its first action was at.
    private string PageVariable(Step step)
    {
        if (_runSymbols.IsDeclared(step.Page)) return step.Page;
        var url = step.Actions.Select(action => action.Source.Url).FirstOrDefault(url => !string.IsNullOrEmpty(url));
        _run.Add(new Line($"{_runSymbols.Assign(step.Page)} = await page.Context.NewPageAsync();",
            $"nothing the flow does opens {step.Page}, as what opened it was left out or not recorded: the flow opens it here, " +
            $"in a new tab{(url is null ? "" : " at the address it was at")}; check that the flow acts on the page it should"));
        if (url is not null) _run.Add(new Line($"await {step.Page}.GotoAsync({Address(url)});"));
        return step.Page;
    }

    private void WriteBody(Step step, bool last)
    {
        if (step.Loop is { } loop)
        {
            WriteLoop(step, loop, last);
            return;
        }

        for (var index = 0; index < step.Actions.Count; index++)
        {
            var action = step.Actions[index];
            _step.Actions = [action.Source.Id];
            WriteIsolated(action, step, index == step.Actions.Count - 1, last);
        }
    }

    // A dialog that was open before the recording, or opened unseen, is declared where the flow first needs it.
    private string DialogVariable(Step step, string title)
    {
        if (_dialogVariables.TryGetValue(title, out var variable)) return variable;
        variable = _runSymbols.New("dialog");
        _dialogVariables[title] = variable;
        var info = step.Actions.Select(action => action.Source.Dialog).FirstOrDefault(dialog => dialog?.Title == title);
        var (locator, check) = DialogLocator(step.Page, info);
        _run.Add(new Line($"var {variable} = {locator};", check));
        return variable;
    }

    private string NewDialogVariable(Step step)
    {
        var opensMany = _plan.Steps.Count(other => other.Opens is not null) > 1;
        var subject = step.Name.StartsWith("Open", StringComparison.Ordinal) ? step.Name[4..^5] : "";
        return _runSymbols.New(opensMany && subject.Length > 0 ? CSharp.Camel(subject) + "Dialog" : "dialog");
    }

    // ── Loops ────────────────────────────────────────────────────────────────────────────────────────────────────

    // The first repeat, for each item of the loop's list, or as many times as its count says. Each line stands for the
    // action of every repeat it does. In the loop, a check stays when it reads the item; one of what every repeat
    // showed alike follows the loop.
    private void WriteLoop(Step step, Loop loop, bool last)
    {
        var repeats = step.Actions.Chunk(loop.Length).ToList();
        var template = repeats[0];
        string header;
        if (loop.List is { } list)
        {
            var item = list.ItemType is { } type ? CSharp.Camel(type) : LoopVariable(LoopFinder.Singular(list.Fields![0].Name));
            item = _step.Symbols.New(item);
            var values = LoopFinder.ValuesOf(template);
            for (var index = 0; index < values.Count; index++)
                _step.LoopValues[values[index]] = list.ItemType is null ? item : $"{item}.{list.Fields![index].Name}";
            foreach (var action in template.Where(action => LoopFinder.RowFromValue(action, template)))
                _step.LoopValues[action.RowKey!] = _step.LoopValues.First(value => value.Key.Text == action.RowKey!.Text).Value;
            header = $"foreach (var {item} in {FromSettings(list)})";
        }
        else
        {
            var counter = _step.Symbols.New("i");
            header = $"for (var {counter} = 0; {counter} < {FromSettings(loop.Count!)}; {counter}++)";
        }

        // A dialog each round opens is found once, before the loop, and what is done in it from there.
        foreach (var opened in template.SelectMany(action => action.Effects).Where(effect => effect.Kind == "dialogOpened")
                     .Select(effect => effect.Dialog).OfType<CapturedDialog>().DistinctBy(dialog => dialog.Title))
        {
            var (locator, check) = DialogLocator(_step.Scope.Page, opened);
            var variable = _step.Symbols.New(CSharp.Camel(CSharp.Pascal(opened.Title) is { } subject && CSharp.IsAsciiText(opened.Title) ? subject + "Dialog" : "dialog"));
            _step.BeforeLoop.Add(new Line($"var {variable} = {locator};", check) { Actions = [template[0].Source.Id] });
            _step.LoopDialogs[opened.Title] = variable;
        }

        var bodyStart = _step.Lines.Count;
        _step.Depth = 1;
        for (var index = 0; index < template.Length; index++)
        {
            _step.Actions = [.. repeats.Select(repeat => repeat[index].Source.Id)];
            _step.Repeats = (repeats, index);
            WriteIsolated(template[index], step, isLast: false, last);
        }

        _step.Repeats = null;
        _step.Depth = 0;
        var body = _step.Lines.GetRange(bodyStart, _step.Lines.Count - bodyStart);
        _step.Lines.RemoveRange(bodyStart, body.Count);
        _step.Actions = [];
        _step.Lines.AddRange(_step.BeforeLoop);
        _step.Add(header);
        if (body.Count == 1)
            _step.Lines.Add(body[0]);
        else
        {
            _step.Add("{");
            _step.Lines.AddRange(body);
            _step.Add("}");
        }

        // Each repeat may have left its message on the page, so there may be more of it than one.
        _step.LoopValues.Clear();
        foreach (var (index, alert) in AlertsEveryRepeatShowedAlike(repeats).Where(_ => _autoChecks))
        {
            _step.Actions = [repeats[^1][index].Source.Id];
            AddCheck($"alert:{alert.Text}", $"alert “{WithoutButton(alert.Text!)}”", AlertExpectation(alert, _step.Scope, many: alert.AlertCount > 1));
        }

        // A check after the last repeat reads what it typed from the list's last item.
        var lastRepeat = repeats[^1];
        if (loop.List is { } typed)
        {
            var values = LoopFinder.ValuesOf(lastRepeat);
            for (var index = 0; index < values.Count; index++)
            {
                var code = $"{FromSettings(typed)}[^1]{(typed.ItemType is null ? "" : $".{typed.Fields![index].Name}")}";
                _step.LoopValues[values[index]] = code;
                // A later step that shows what the last round typed, as a link to the item it added, reads it there too.
                if (values[index] is { Type: "string", Text.Length: >= 3 } value) _afterLoops[value.Text!] = code;
            }
        }

        _step.Actions = [lastRepeat[^1].Source.Id];
        WriteChecks(lastRepeat[^1]);
        _step.LoopValues.Clear();
    }

    // The last word of the item's name, as a person names the item of a list: NewTask gives task.
    private static string LoopVariable(string name) =>
        CSharp.Camel(LastWord().Match(name).Value is { Length: > 0 } word ? word : name);

    private static IEnumerable<(int Index, CapturedEffect Alert)> AlertsEveryRepeatShowedAlike(List<PlannedAction[]> repeats)
    {
        for (var index = 0; index < repeats[0].Length; index++)
        {
            var alerts = repeats.Select(repeat => FirstAlert(repeat[index])).ToList();
            if (alerts.All(alert => alert is not null) && alerts.Select(alert => alert!.Text).Distinct().Count() == 1)
                yield return (index, alerts[^1]!);
        }
    }

    /// <summary>What to check by hand starts with this for an action the writer could not write, which it left out.</summary>
    public const string NotWritten = "could not write action";

    // An action the writer cannot write is left out, its lines taken back, and said to redo by hand: the rest of the
    // flow is written still, as a recording the recorder cannot write whole must still be saved.
    private void WriteIsolated(PlannedAction action, Step step, bool isLast, bool last)
    {
        var (lines, beforeLoop, declared) = (_step.Lines.Count, _step.BeforeLoop.Count, _step.Declared.Keys.ToHashSet());
        try
        {
            WriteAction(action, step, isLast, last);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            _step.Lines.RemoveRange(lines, _step.Lines.Count - lines);
            _step.BeforeLoop.RemoveRange(beforeLoop, _step.BeforeLoop.Count - beforeLoop);
            foreach (var local in _step.Declared.Keys.Where(key => !declared.Contains(key)).ToList()) _step.Declared.Remove(local);
            if (last)
                _plan.NotRecorded.Add($"{NotWritten} {action.Source.Id} (a '{action.Kind}') in {step.Name}, as {failure.GetType().Name}: " +
                                      $"{failure.Message} Redo it by hand.");
        }
    }

    // ── Actions ──────────────────────────────────────────────────────────────────────────────────────────────────

    private void WriteAction(PlannedAction action, Step step, bool isLast, bool last)
    {
        var source = action.Source;
        var scope = _step.Scope;
        // A browser dialog is accepted by one handler a page has once, however often it shows: a second one would accept
        // the same dialog again, which throws. Before a loop whose rounds show one.
        if (action.Has("jsDialog") && _acceptsDialogs.Add(source.Page))
        {
            var lines = _step.Depth > 0 ? _step.BeforeLoop : _step.Lines;
            lines.Add(new Line($"{scope.Page}.Dialog -= {AcceptBrowserDialog};") { Actions = _step.Actions.Take(1).ToList() });
            lines.Add(new Line($"{scope.Page}.Dialog += {AcceptBrowserDialog};",
                "a browser dialog (alert or confirm) showed here: the flow accepts each one from here on; dismiss it where you did")
                { Actions = _step.Actions.Take(1).ToList() });
        }

        // A menu that opened as the pointer rested is opened so: by hovering what the pointer was on.
        var opener = source.HoverMenu == true && LocatorPolicy.Choose(source.HoverOpener) is { } hovered ? hovered : null;
        if (opener is not null)
        {
            var why = LocatorPolicy.Problem(opener);
            _step.Add($"await {Render(opener, source)}{Break}.HoverAsync();", why);
        }

        var problem = source.HoverMenu == true && opener is null
            ? "the menu it is in opened on hover: hover what opens it first (HoverAsync)"
            : null;
        var from = _step.Lines.Count;
        switch (action.Kind)
        {
            case "click" or "dblclick":
            {
                var method = action.Kind == "click" ? "ClickAsync" : "DblClickAsync";
                if (action.Effects.FirstOrDefault(effect => effect.Kind == "popup") is { Page: { } popup })
                {
                    var locator = Locate(action, ref problem);
                    _step.Add($"{Declared(popup)} = await {scope.Page}.RunAndWaitForPopupAsync(() => {locator}.{method}());",
                        Join(problem, "a pop-up opened here: check what the flow does in it"));
                }
                else
                {
                    ScrollTo(source);
                    _step.Add($"await {Locate(action, ref problem)}{Break}.{method}();", problem);
                    // In a loop, a check stays only when it reads the loop's item.
                    if (_autoChecks && _step.Depth == 0 && action.Has("tabSelected"))
                        AddCheck("selected", "tab selected", $"await Expect({Locate(action, ref problem)}){Break}" +
                                                             ".ToHaveAttributeAsync(\"aria-selected\", \"true\");");
                }

                break;
            }
            case "fill":
                _step.Add($"await {Locate(action, ref problem)}{Break}.FillAsync({AsText(action.Value)});", problem);
                // A picker that keeps a date only once its OK commits it, as antd's, takes Enter as its OK.
                if (source.Committed == true) _step.Add($"await {Locate(action, ref problem)}{Break}.PressAsync(\"Enter\");");
                break;
            // A checkbox under the box drawn over it is ticked through that box, as the person clicked it.
            case "check" or "uncheck" when source.Widget == "radio":
                _step.Add($"await {Locate(action, ref problem)}{Break}.CheckAsync({(Covered(source) ? ForceOptions : "")});", problem);
                break;
            // In a loop whose every round ticked it alike, or a checkbox of a group the loop goes over, as each round did.
            case "check" or "uncheck" when action.Value is not { Flag: not null }:
                _step.Add($"await {Locate(action, ref problem)}{Break}.{(action.Kind == "check" ? "CheckAsync" : "UncheckAsync")}({(Covered(source) ? ForceOptions : "")});", problem);
                break;
            case "check" or "uncheck":
                _step.Add($"await {Locate(action, ref problem)}{Break}.SetCheckedAsync({Setting(action.Value)}{(Covered(source) ? ", " + ForceOptions : "")});", problem);
                break;
            case "choose":
                WriteChoice(action, problem);
                break;
            case "press":
                if (source.OnPage == true || action.Locator is null)
                    _step.Add($"await {scope.Page}.Keyboard.PressAsync({CSharp.Literal(source.Key ?? "Enter")});",
                        source.Guessed
                            ? "the dialog closed with no action the recorder saw (a key, a click beside it): the flow presses Escape; check that it closes it"
                            : null);
                else
                {
                    var locator = Locate(action, ref problem);
                    _step.Add($"await {locator}{Break}.PressAsync({CSharp.Literal(source.Key ?? "Enter")});", problem);
                }

                break;
            case "upload" when action.Value is { } file:
                _step.Add($"await {Locate(action, ref problem)}{Break}.SetInputFilesAsync({Setting(file)});",
                    Join(problem, $"upload: the recorder saw only the file's name; set {file.Name} to its full path in each case"));
                break;
            case "upload":
                _step.Add($"await {Locate(action, ref problem)}{Break}.SetInputFilesAsync({Files(source.Files ?? [])});",
                    Join(problem, "upload: the recorder saw only the file names; return each one's full path in FileToUpload"));
                break;
            case "drag":
            {
                var locator = Locate(action, ref problem);
                if (source.To is { } to && LocatorPolicy.Choose(to) is { } drop)
                    _step.Add($"await {locator}{Break}.DragToAsync({Render(drop, source)});",
                        Join(problem, "drag and drop: check where it drops"));
                else
                    _step.Add($"await {locator}{Break}.HoverAsync();",
                        Join(problem, "a drag the recorder could not follow: add DragToAsync() to where you dropped it"));
                break;
            }
            case "navigate":
                _step.Add($"await {scope.Page}.GotoAsync({Address(source.Url ?? _plan.StartUrl)});",
                    source.AfterPause == true
                        ? "the recording was paused, and the app was at this address when it resumed: the flow goes there " +
                          "by the address; check that it can, as what led there was not recorded"
                        : null);
                break;
            case "newTab":
                _step.Add($"{Declared(source.Page)} = await {scope.Page}.Context.NewPageAsync();",
                    source.AfterPause == true
                        ? "a tab that opened while the recording was paused: the flow opens it here"
                        : "a tab you opened yourself: the flow opens it here");
                if (source.Url is { } opened) _step.Add($"await {source.Page}.GotoAsync({Address(opened)});");
                break;
            case "expect":
                WriteCheck(source);
                break;
            default:
                if (last)
                    _plan.NotRecorded.Add($"a '{action.Kind}' the recorder did not understand, in {step.Name}: redo it by hand");
                break;
        }

        if (action.Kind is not ("click" or "dblclick") && action.Effects.FirstOrDefault(effect => effect.Kind == "popup") is { Page: { } opensPopup })
            WaitForPopup(opensPopup, from);
        Saw(action);
        WriteEffects(action, step, isLast);
    }

    // A pop-up that what is no click opened, as Enter in a field does: the statement last written for the action is run
    // and waited for, as a click is; else, when nothing was written for it, the pop-up is the page opened last.
    private void WaitForPopup(string popup, int from)
    {
        var page = _step.Scope.Page;
        if (_step.Lines.Count > from && _step.Lines[^1] is { Depth: 0, Mark.Length: 0, Text: var text } line
                                     && text.StartsWith("await ", StringComparison.Ordinal) && text.EndsWith(';'))
        {
            _step.Lines[^1] = line with
            {
                Text = $"{Declared(popup)} = await {page}.RunAndWaitForPopupAsync(() => {text["await ".Length..^1]});",
                Check = Join(line.Check, "a pop-up opened here: check what the flow does in it"),
            };
            return;
        }

        _step.Add($"{Declared(popup)} = {page}.Context.Pages[^1];",
            "a pop-up opened here, by what the recorder did not understand: the flow takes the page opened last; check that it is the pop-up");
    }

    // A page the action opens, as a variable of the step: the step's own, when the page it is on has that name, as a
    // tab opened again of a name opened before.
    private string Declared(string page) => _step.Symbols.Assign(page);

    // What the action read is known to what follows it: a text that shows it, or a locator that shows what was typed.
    // Not a loop's item, which only its repeat knows.
    private void Saw(PlannedAction action)
    {
        if (_step.Depth > 0) return;
        // What a login typed, a user name, names no record a later locator finds: a menu "Admin" is no user "Admin".
        var login = _plan.Steps.FirstOrDefault(step => step.Actions.Contains(action))?.Actions.Any(other => other.Value?.Secret == true) == true;
        foreach (var setting in action.Values.Append(action.RowKey).OfType<Setting>().Where(_settings.Contains))
        {
            if (!_seen.Contains(setting)) _seen.Add(setting);
            var typed = setting == action.SearchValue || (action.Kind == "fill" && setting == action.Value);
            if (typed && !login && !_typed.Contains(setting)) _typed.Add(setting);
        }
    }

    private void WriteChoice(PlannedAction action, string? problem)
    {
        var source = action.Source;
        var scope = _step.Scope;
        // In its own list, when another list has an option of that name too.
        var list = source.Option?.Candidates.FirstOrDefault(candidate => candidate is { Kind: "role", Matches: true }
            && candidate.Parts is [.., { M: "role", Role: "listbox" }, { M: "role", Role: "option" }])?.Parts[^2];
        var inList = list is null ? "" : LocatorPolicy.Render("", [list], breakBefore: Break);
        // An option with no role, among those of its kind that show, by its text: antd's list keeps closed ones, hidden.
        var option = source.OptionCss is { } optionCss
            ? $"{scope.Page}.Locator({CSharp.Literal(optionCss + ":visible")}){Break}.GetByText({Setting(action.OptionValue ?? action.Value)}, new() {{ Exact = true }})"
            : $"{scope.Page}{inList}.GetByRole(AriaRole.Option, new() {{ Name = {Setting(action.OptionValue ?? action.Value)}, Exact = true }})";
        var optionProblem = source.Option is null || source.OptionCss is not null || source.Option.Candidates.Any(candidate =>
            candidate is { Kind: "role", Matches: true, Parts: [.., { Exact: true } part] } && part.Name == source.Value)
            ? null
            : "the option's name did not find it alone when recorded";
        switch (source.Widget)
        {
            case "select" or "autocomplete":
                // What the person typed to find the option, the flow types too, then chooses the option by its own text.
                if (source.Widget == "autocomplete" && (action.Search is not null || action.SearchValue is not null))
                    _step.Add($"await {Locate(action, ref problem)}{Break}.FillAsync({Setting(action.SearchValue ?? action.OptionValue ?? action.Value)});", problem);
                // An option of no role is chosen in the list the click before it opened, as is one of a list opened already.
                else if (source.OptionCss is null && !action.Opened)
                    _step.Add($"await {Locate(action, ref problem)}{Break}.ClickAsync();", problem);
                ScrollTo(source);
                _step.Add($"await {option}{Break}.ClickAsync();", optionProblem);
                // In a loop, a check stays only when it reads the loop's item. A field that showed nothing of the choice,
                // such as an input under the text that shows it, is not checked; an input that holds it, by its value.
                var settled = source.Effects.FirstOrDefault(effect => effect.Kind == "settled");
                var byValue = source.Widget == "autocomplete" || source.Field?.Tag == "input";
                if (_autoChecks && settled is not { Value: null or "" }
                    && (_step.Depth == 0 || _step.LoopValues.ContainsKey(action.ShownValue ?? action.Value!)))
                {
                    var field = Locate(action, ref problem);
                    AddCheck("shows", $"shows “{(action.ShownValue ?? action.Value)!.Value}”", byValue
                        ? $"await Expect({field}){Break}.ToHaveValueAsync({Setting(action.Value)});"
                        : $"await Expect({field}){Break}.ToHaveTextAsync({Setting(action.ShownValue ?? action.Value)});");
                }

                break;
            case "native":
            {
                var locator = Locate(action, ref problem);
                _step.Add($"await {locator}{Break}.SelectOptionAsync(new SelectOptionValue {{ Label = {Setting(action.Value)} }});",
                    problem);
                break;
            }
            default:
                ScrollTo(source);
                _step.Add($"await {option}{Break}.ClickAsync();", optionProblem);
                break;
        }
    }

    // A list that renders only the items in sight is scrolled as far as the person scrolled it, so the item is there.
    private void ScrollTo(CapturedAction source)
    {
        if (source.Scrolled is not { List: { } list, Top: > 0 } scrolled || LocatorPolicy.Choose(list) is not { } found) return;
        _step.Add($"await {Render(found, source)}{Break}.HoverAsync();");
        _step.Add($"await {_step.Scope.Page}.Mouse.WheelAsync(0, {scrolled.Top});",
            "a list that shows only the items in sight: the flow scrolls it as far as you did; check that this reaches the item");
    }

    // What the action changed: the dialog it closed, the address it went to (unless a stronger check says the same),
    // the page's wait, the message it showed; then the checks the person chose after it; and for the step's last action,
    // the dialog it opened, which the person's checks come after, or the page.
    private void WriteEffects(PlannedAction action, Step step, bool isLast)
    {
        var scope = _step.Scope;
        var repeats = _step.Repeats;
        var closed = action.Effects.Where(effect => effect.Kind == "dialogClosed").DistinctBy(effect => effect.Dialog?.Title)
            .ToList();
        // A dialog a loop's round opened shows, as each round's did.
        foreach (var showed in action.Effects.Where(effect => effect.Kind == "dialogOpened" && _step.Depth > 0))
            if (showed.Dialog?.Title is { } showedTitle && _step.LoopDialogs.TryGetValue(showedTitle, out var shown))
                AddCheck("opened", "dialog shows", $"await Expect({shown}){Break}.ToBeVisibleAsync();");

        foreach (var dialog in closed.Where(_ => _autoChecks))
        {
            var own = dialog.Dialog?.Title == scope.Dialog && scope.Dialog is not null;
            var locator = own ? "dialog"
                : dialog.Dialog?.Title is { } closedTitle && _step.LoopDialogs.TryGetValue(closedTitle, out var looped) ? looped
                : DialogLocator(scope.Page, dialog.Dialog).Locator;
            var said = own || dialog.Dialog?.Title is not { Length: > 0 } title ? "dialog closes" : $"“{title}” closes";
            AddCheck($"closed:{dialog.Dialog?.Title}", said, $"await Expect({locator}){Break}.ToBeHiddenAsync({SlowTimeout});");
        }

        var alerts = action.Effects.Where(IsAlert).DistinctBy(effect => effect.Text).ToList();
        var shownElsewhere = action.Has("dialogOpened") || action.Has("tabSelected") || (closed.Count > 0 && alerts.Count > 0);
        // An address change the recorder did not see the address of is no check.
        var changes = action.Effects.Where(effect => _autoChecks && effect is { Kind: "urlChanged", Url.Length: > 0 });
        // Not one the address before the action matched already, as a sort's query does: it would check nothing.
        foreach (var changed in changes.TakeLast(shownElsewhere ? 0 : 1)
                     .Where(changed => action.Source.Url is not { } before || !Regex.IsMatch(before, UrlPattern(changed.Url!))))
        {
            // On the pop-up the action opened, when it went there; else on the page the step is on, as no other is known in it.
            var page = changed.Page is { } alias && alias != action.Source.Page
                                                 && action.Effects.Any(effect => effect.Kind == "popup" && effect.Page == alias)
                ? alias
                : scope.Page;
            AddCheck("url", $"URL {StepNamer.Route(changed.Url) ?? changed.Url}",
                $"await Expect({page}){Break}.ToHaveURLAsync(new Regex({CSharp.Literal(UrlPattern(changed.Url!))}));");
        }

        // In a loop, the page is waited for where any repeat's was.
        var busy = (repeats is var (all, at) ? all.Select(repeat => repeat[at]) : [action]).SelectMany(done => done.Effects)
            .Where(effect => effect.Kind == "busy").Select(effect => effect.Value).Distinct();
        foreach (var kind in busy)
        {
            var indicator = kind == "ariaBusy"
                ? $"{scope.Page}.Locator(\"[aria-busy='true']:visible\")"
                : $"{scope.Page}.GetByRole(AriaRole.Progressbar)";
            _step.Add($"await Expect({indicator}){Break}.ToHaveCountAsync(0, {SlowTimeout});");
        }

        // In a loop, a message is checked where it shows the item, as each repeat's did.
        foreach (var alert in alerts.Where(alert => _autoChecks && (repeats is null || ShowsTheItem(alert, repeats.Value))))
            AddCheck($"alert:{alert.Text}", $"alert “{WithoutButton(alert.Text!)}”", AlertExpectation(alert, scope));

        // In a loop, the checks after the last repeat follow the loop.
        if (isLast && step.Opens is { } opened)
        {
            var (locator, check) = DialogLocator(scope.Page, opened);
            var shows = _autoChecks && !Dropped("opened");
            if (_autoChecks) Expect(new Expectation("opened", "dialog shows", Auto: true) { Dropped = !shows });
            if (!shows && action.Checks.Count == 0)
            {
                _step.Add($"return {locator};", check);
                return;
            }

            var variable = _step.Symbols.New(scope.Dialog is null ? "dialog" : "opened");
            _step.Add($"var {variable} = {locator};", check);
            _step.Opened = (opened.Title, variable);
            if (shows) _step.Add($"await Expect({variable}){Break}.ToBeVisibleAsync();", mark: AutoCheck);
            WriteChecks(action);
            _step.Add($"return {variable};");
            return;
        }

        if (_step.Depth == 0) WriteChecks(action);
        if (isLast && step.OpensPage is { } opensPage) _step.Add($"return {opensPage};");
    }

    // An auto check of what the action did, unless the person dropped it; the panel lists it either way.
    private void AddCheck(string key, string text, string code)
    {
        if (!_autoChecks) return;
        var dropped = Dropped(key);
        Expect(new Expectation(key, text, Auto: true) { Dropped = dropped });
        if (!dropped) _step.Add(code, mark: AutoCheck);
    }

    private bool Dropped(string key) => _step.Actions.Any(id => _dropped.TryGetValue(id, out var keys) && keys.Contains(key));

    // Told for each action the line is written for, once the step is written for good.
    private void Expect(Expectation expectation)
    {
        if (_step.Uses is null) return;
        foreach (var id in _step.Actions)
            (_expects.TryGetValue(id, out var list) ? list : _expects[id] = []).Add(expectation);
    }

    // A message, not a busy one that goes by itself ("Loading...", "Please wait"), which a check would race.
    internal static bool IsAlert(CapturedEffect effect) => effect is { Kind: "alert", Text.Length: > 0 } && !Passing().IsMatch(effect.Text);

    /// <summary>A message without the words of the button it holds, as MUI's snackbar ends with UNDO.</summary>
    public static string WithoutButton(string text) => AlertButton().Replace(text, "");

    private static CapturedEffect? FirstAlert(PlannedAction action) => action.Effects.FirstOrDefault(IsAlert);

    // A message that names a value the first repeat typed, as each repeat's named its own.
    private static bool ShowsTheItem(CapturedEffect alert, (List<PlannedAction[]> All, int Index) repeats)
    {
        var first = repeats.All[0].SelectMany(action => action.Values).ToList();
        if (!first.Any(value => value.Text is { Length: >= 3 } text && alert.Text!.Contains(text, StringComparison.Ordinal)))
            return false;
        return repeats.All.All(repeat =>
        {
            var expected = alert.Text!;
            foreach (var (value, other) in first.Zip(repeat.SelectMany(action => action.Values)))
                if (value.Text is { Length: >= 3 } recorded && other.Text is { } typed)
                    expected = expected.Replace(recorded, typed, StringComparison.Ordinal);
            return FirstAlert(repeat[repeats.Index])?.Text == expected;
        });
    }

    // A message is found by its text, with the recorded values in it read from the settings. A number left in it is a
    // record's own, so it is found by the longest run of words around the numbers, one that reads a setting first.
    private string AlertExpectation(CapturedEffect alert, Scope scope, bool many = false)
    {
        var alerts = $"{scope.Page}.GetByRole(AriaRole.Alert)";
        var shows = many ? ".Not.ToHaveCountAsync(0)" : ".ToBeVisibleAsync()";
        var pieces = Pieces(WithoutButton(alert.Text!));
        if (!pieces.Where(piece => piece.Code is null).Any(piece => piece.Text.Any(char.IsDigit)))
            return $"await Expect({alerts}{Break}.Filter(new() {{ HasText = {Code(pieces)} }})){Break}{shows};";
        if (WordsAroundNumbers(pieces) is { } words)
            return $"await Expect({alerts}{Break}.Filter(new() {{ HasText = {Code(words)} }})){Break}{shows};";
        return alert.AlertCount is null or <= 1
            ? $"await Expect({alerts}){Break}.ToBeVisibleAsync();"
            : $"await Expect({alerts}){Break}.Not.ToHaveCountAsync(0);";
    }

    private static List<(string Text, string? Code)>? WordsAroundNumbers(List<(string Text, string? Code)> pieces)
    {
        var runs = new List<List<(string Text, string? Code)>> { new() };
        foreach (var piece in pieces)
        {
            if (piece.Code is not null)
            {
                runs[^1].Add(piece);
                continue;
            }

            var parts = NumberWord().Split(piece.Text);
            for (var index = 0; index < parts.Length; index++)
            {
                if (index > 0) runs.Add([]);
                if (parts[index].Length > 0) runs[^1].Add((parts[index], null));
            }
        }

        // Of runs as long, the last: "Order 5521 saved" is found by "saved".
        var trimmed = runs.Select(Trim).Select((run, index) => (Run: run, Index: index))
            .Where(found => found.Run.Count > 0 && (found.Run.Any(piece => piece.Code is not null)
                                                    || found.Run.Sum(piece => piece.Text.Count(char.IsLetter)) >= 3)).ToList();
        return trimmed.OrderByDescending(found => found.Run.Any(piece => piece.Code is not null))
            .ThenByDescending(found => found.Run.Sum(piece => piece.Text.Length)).ThenByDescending(found => found.Index)
            .Select(found => found.Run).FirstOrDefault();

        static List<(string Text, string? Code)> Trim(List<(string Text, string? Code)> run)
        {
            const string edges = " (:-,#";
            var list = run.ToList();
            if (list is [({ } text, null), ..]) list[0] = (text.TrimStart(edges.ToCharArray()), null);
            if (list is [.., ({ } end, null)]) list[^1] = (end.TrimEnd(edges.ToCharArray()), null);
            return [.. list.Where(piece => piece.Code is not null || piece.Text.Length > 0)];
        }
    }

    // The dialog by its title. A title with a record's number in it is found by the words before the number, unless
    // the number is a recorded value.
    private (string Locator, string? Check) DialogLocator(string page, CapturedDialog? dialog)
    {
        const string role = "GetByRole(AriaRole.Dialog";
        if (dialog is null) return ($"{page}.{role})", "the recorder did not see this dialog's title");
        // By its title without the Close button in its title bar, which is part of the name it has, so finds it still.
        var pieces = Pieces(dialog.TitleMatches ? StepNamer.WithoutClose(dialog.Title) : dialog.Title);
        var hasNumber = pieces.Where(piece => piece.Code is null).Any(piece => piece.Text.Any(char.IsDigit));
        if (dialog.TitleMatches && !hasNumber) return ($"{page}.{role}, new() {{ Name = {Code(pieces)} }})", null);
        if (dialog is { PrefixMatches: true, Prefix: { } prefix })
            return ($"{page}.{role}, new() {{ Name = {CSharp.Literal(prefix)} }})", null);
        if (dialog.TitleMatches) return ($"{page}.{role}, new() {{ Name = {CSharp.Literal(dialog.Title)} }})", null);
        if (dialog is { RawMatches: true, Raw: { } raw })
            return ($"{page}.{role}, new() {{ Name = {CSharp.Literal(raw)}, Exact = true }})", null);
        // A dialog with no name, by the heading it shows: only it has an element of that text.
        var byHeading = dialog.Heading is { } heading
            ? LocatorPolicy.Render(page, [new LocatorPart { M = "role", Role = "dialog", Heading = heading }],
                _ => Code(Pieces(heading)), Break)
            : null;
        if (byHeading is not null && dialog.HeadingMatches) return (byHeading, null);
        if (dialog.UnnamedMatches) return ($"{page}.{role})", null);
        if (byHeading is not null) return (byHeading, "this dialog's heading did not find it alone when recorded");
        if (dialog.Title.Length == 0) return ($"{page}.{role})", "this dialog has no title, and another dialog was open too");
        return ($"{page}.{role}, new() {{ Name = {CSharp.Literal(dialog.Title)} }})",
            "this dialog's title did not find it alone when recorded");
    }

    // ── Checks the person chose ──────────────────────────────────────────────────────────────────────────────────

    private void WriteChecks(PlannedAction action)
    {
        foreach (var check in action.Checks) WriteCheck(check);
    }

    // What the person chose to check (F6), marked "// check": what an element shows, its items or its row, or the
    // page's address. A value read before reads its setting; a number left in a text is a record's own, which changes
    // from run to run, so the words around it are expected, as a message is found.
    private void WriteCheck(CapturedAction check)
    {
        if (check.Check is "value" or "text" or "contains" && (check.Secret is not null || SecretField.Is(check.Target)))
        {
            WriteSecretCheck(check);
            return;
        }

        var expected = check.Value ?? "";
        string? problem = null;
        string Element() => Located(check, check.Target, ref problem);

        // An element found by the very text it is checked to have would pass while it is there: it is checked to show.
        var byItsText = check.Check is "text" or "contains" && LocatorPolicy.Choose(check.Target) is { } found
                        && expected.Length > 0 && !expected.Any(char.IsDigit) && (found.Parts.Any(part => part.Name == expected || part.Text == expected || part.HasText == expected)
                                                   || found.Csharp?.Contains(CSharp.Literal(expected), StringComparison.Ordinal) == true);
        string Shows(Func<string, string> whole, Func<string, string> words) => byItsText ? "ToBeVisibleAsync()" : Expected(expected) switch
        {
            ({ } code, false) => whole(code),
            ({ } code, true) => words(code),
            _ => "Not.ToBeEmptyAsync()",
        };

        var (line, said) = check.Check switch
        {
            "url" => ($"await Expect({PageOf(check)}){Break}.ToHaveURLAsync(new Regex({CSharp.Literal(UrlPattern(expected))}));",
                $"URL {StepNamer.Route(expected) ?? expected}"),
            "count" => ($"await Expect({Located(check, check.Group, ref problem)}){Break}.ToHaveCountAsync({CountOf(expected)});",
                $"count is {CountOf(expected)}"),
            "row" => ($"await Expect({Located(check, check.Group, ref problem)}){Break}.Not.ToHaveCountAsync(0);",
                "a row of " + string.Join(", ", check.Group?.Candidates.FirstOrDefault()?.Parts.Where(part => part.M == "filter")
                    .Select(part => $"“{part.HasText}”") ?? [])),
            var kind => ($"await Expect({Element()}){Break}.{kind switch
            {
                "hidden" => "ToBeHiddenAsync()",
                "text" => Shows(code => $"ToHaveTextAsync({code})", code => $"ToContainTextAsync({code})"),
                "contains" => Shows(code => $"ToContainTextAsync({code})", code => $"ToContainTextAsync({code})"),
                "value" => Shows(code => $"ToHaveValueAsync({code})", code => $"ToHaveValueAsync(new Regex(Regex.Escape({code})))"),
                "checked" => "ToBeCheckedAsync()",
                "unchecked" => "Not.ToBeCheckedAsync()",
                "enabled" => "ToBeEnabledAsync()",
                "disabled" => "ToBeDisabledAsync()",
                _ => "ToBeVisibleAsync()",
            }};", kind switch
            {
                "hidden" => "is gone",
                "text" or "contains" when byItsText => $"shows “{expected}”",
                "text" => $"has text “{expected}”",
                "contains" => $"contains “{expected}”",
                "value" => $"has value “{expected}”",
                "checked" or "unchecked" or "enabled" or "disabled" => kind,
                _ => "shows",
            }),
        };
        _step.Add(line, problem, mark: ChosenCheck);
        Expect(new Expectation(check.Id, said, Auto: false)
        {
            Of = check.Check is "url" or "row" ? null : LocatorPolicy.Describe(LocatorPolicy.Choose(check.Group ?? check.Target)),
        });
    }

    // What a secret's field holds is checked against the secret's setting the flow typed into that field, never against
    // its text, which no file keeps; with none, that the field is not empty.
    private void WriteSecretCheck(CapturedAction check)
    {
        string? problem = null;
        var element = Located(check, check.Target, ref problem);
        var key = LocatorPolicy.Choose(check.Target) is { } found
            ? found.Kind == "generator" ? found.Csharp : LocatorPolicy.Render("page", found.Parts)
            : null;
        var typed = _plan.Steps.SelectMany(step => step.Actions)
            .LastOrDefault(action => action is { Kind: "fill", Value.Secret: true } && action.Key == key && _seen.Contains(action.Value!))?.Value;
        var line = typed is null
            ? $"await Expect({element}){Break}.Not.ToBeEmptyAsync();"
            : $"await Expect({element}){Break}.ToHaveValueAsync({Setting(typed)});";
        _step.Add(line, Join(problem, typed is null ? "a secret's value is never kept: the flow checks only that its field is not empty" : null),
            mark: ChosenCheck);
        Expect(new Expectation(check.Id, typed is null ? "is not empty" : $"holds {typed.Name}", Auto: false)
        {
            Of = LocatorPolicy.Describe(LocatorPolicy.Choose(check.Target)),
        });
    }

    // The element a check is of, every item it is one of, or its row by the cells chosen, as the recorder found it
    // alone. A name or text in it reads the values typed before from their settings. Of a row's cells, one that is only a
    // record's number, which changes from run to run, is left out, and the words around one are kept.
    private string Located(CapturedAction check, CapturedTarget? target, ref string? problem)
    {
        var candidate = LocatorPolicy.Choose(target);
        problem = LocatorPolicy.Problem(candidate);
        if (candidate is null) return $"{PageOf(check)}.Locator(\"body\")";
        if (candidate.Kind == "generator") return Render(candidate, check);
        var codes = new Dictionary<LocatorPart, string>();
        var parts = new List<LocatorPart>();
        foreach (var part in candidate.Parts)
        {
            var text = part.M == "filter" ? part.HasText : part is { M: "role", Regex: null } ? part.Name : part.M == "text" ? part.Text : null;
            // What the check expects and where it looks read the same settings: a row's key, a value chosen before.
            var code = text is null ? null : check.Check == "row" && part.M == "filter" ? Expected(text).Code : Seen(text);
            if (code is not null) codes[part] = code;
            if (code is not null || check.Check != "row" || part.M != "filter") parts.Add(part);
        }

        return Render(new CapturedCandidate { Kind = candidate.Kind, Parts = parts }, check, part => codes.GetValueOrDefault(part));
    }

    private static string CountOf(string value) => int.TryParse(value, out var count) ? CSharp.Number(count) : "0";

    // The text a check expects as C#, with what it shows of the values read before read from their settings; when a
    // number is left in it, only the words around the numbers (Partial). Null when nothing but numbers is left.
    private (string? Code, bool Partial) Expected(string text)
    {
        var pieces = Pieces(text);
        if (!pieces.Where(piece => piece.Code is null).Any(piece => piece.Text.Any(char.IsDigit))) return (Code(pieces), false);
        return WordsAroundNumbers(pieces) is { } words ? (Code(words), true) : (null, false);
    }

    // ── Locators ─────────────────────────────────────────────────────────────────────────────────────────────────

    // The action's element. One the step uses twice is a local, and so is a row the step finds two elements in; each is
    // declared where it is first used, or before the loop when the loop's item does not change it. What is wrong with
    // the element's locator is said where it is declared.
    private string Locate(PlannedAction action, ref string? problem)
    {
        var why = LocatorPolicy.Problem(action.Locator);
        var readsItem = _step.ReadsItem;
        _step.ReadsItem = false;
        string? NameOf(LocatorPart part) => NameFromSettings(action, part);
        var expression = action.Locator is null
            ? $"{_step.Scope.Page}.Locator(\"body\")"
            : Render(action.Locator, action.Source, NameOf);
        var row = action.Locator is { Kind: "row" } candidate ? InRow(candidate, action.Source, NameOf) : default;
        var changesWithItem = _step.ReadsItem;
        _step.ReadsItem |= readsItem;
        if (_step.Uses is null)
        {
            _step.Counted[expression] = _step.Counted.GetValueOrDefault(expression) + 1;
            if (row.Row is not null)
                (_step.RowsCounted.TryGetValue(row.Row, out var found) ? found : _step.RowsCounted[row.Row] = []).Add(expression);
        }
        else if (_step.Uses.GetValueOrDefault(expression) >= 2)
            return Declare(expression, LocalName(action), why, changesWithItem);
        else if (row.Row is not null && _step.RowUses!.GetValueOrDefault(row.Row)?.Count >= 2)
        {
            var header = CSharp.Pascal(action.Source.Element?.Row?.Header) is { } column ? CSharp.Camel(column) + "Row" : "row";
            expression = LocatorPolicy.Render(Declare(row.Row, header, null, changesWithItem), row.InIt, NameOf, Break);
        }

        problem = Join(why, problem);
        return expression;
    }

    private string Declare(string expression, string name, string? why, bool changesWithItem)
    {
        if (_step.Declared.TryGetValue(expression, out var local)) return local;
        local = _step.Symbols.New(_step.Symbols.IsTaken(name) ? name + "Field" : name);
        _step.Declared[expression] = local;
        var declaration = new Line($"var {local} = {expression};", why) { Actions = _step.Actions };
        // Before the loop, it is the first repeat's.
        if (_step.Depth > 0 && !changesWithItem)
            _step.BeforeLoop.Add(declaration with { Actions = _step.Actions.Take(1).ToList() });
        else _step.Lines.Add(declaration with { Depth = _step.Depth });
        return local;
    }

    // A row's locator, up to the text that finds the row, and the rest of the element's locator in it.
    private (string? Row, List<LocatorPart> InIt) InRow(CapturedCandidate candidate, CapturedAction source,
        Func<LocatorPart, string?> nameOf)
    {
        var (root, parts) = Rooted(candidate, source);
        var filter = parts.FindIndex(part => part.M == "filter");
        return filter < 0 || filter == parts.Count - 1 ? default
            : (LocatorPolicy.Render(root, parts[..(filter + 1)], nameOf, Break), parts[(filter + 1)..]);
    }

    // A local named after the field's setting, or the element's text and role: addLineButton, detailsTab.
    private static string LocalName(PlannedAction action)
    {
        if (action.Value is { } value) return CSharp.Camel(value.Name);
        var target = action.Source.Target;
        var label = StepNamer.Label(action, without: action.RowKey?.Text) is { } text && CSharp.IsAsciiText(text)
            ? CSharp.Pascal(text)
            : null;
        var role = target?.Role switch
        {
            "button" => "Button",
            "link" => "Link",
            "tab" => "Tab",
            "menuitem" => "Item",
            "checkbox" => "Checkbox",
            "textbox" or "combobox" => "Field",
            _ => "",
        };
        if (label is null) return CSharp.Camel(role.Length > 0 ? role : "Element");
        return CSharp.Camel(label.EndsWith(role, StringComparison.Ordinal) ? label : label + role);
    }

    private string Render(CapturedCandidate candidate, CapturedAction source, Func<LocatorPart, string?>? nameOf = null)
    {
        if (candidate.Kind == "generator") return LocatorPolicy.RenderGenerated(PageOf(source), candidate);
        var (root, parts) = Rooted(candidate, source);
        return LocatorPolicy.Render(root, parts, nameOf, Break);
    }

    // Found in the step's own dialog: from the dialog the step is given; in the dialog the step opened, as a check after
    // it is, from its variable.
    private (string Root, List<LocatorPart> Parts) Rooted(CapturedCandidate candidate, CapturedAction source)
    {
        var scope = _step.Scope;
        var parts = candidate.Parts;
        if (parts is not [{ M: "role", Role: "dialog" }, ..] || source.Dialog?.Title is not { } title) return (PageOf(source), parts);
        if (scope.Dialog is not null && title == scope.Dialog) return (scope.Root, parts[1..]);
        if (_step.LoopDialogs.TryGetValue(title, out var looped)) return (looped, parts[1..]);
        return _step.Opened is { } opened && opened.Title == title ? (opened.Variable, parts[1..]) : (PageOf(source), parts);
    }

    // The page an action or a check is on: the step's, or a pop-up or tab it opened.
    private string PageOf(CapturedAction source) => source.Page == _step.Scope.Alias ? _step.Scope.Page : source.Page;

    // A radio is found by the option's name or value and a row by its key, all read from the settings; in a loop, a name
    // with the item's value in it reads the item.
    private string? NameFromSettings(PlannedAction action, LocatorPart part)
    {
        if (part.M == "filter" && action.RowKey is { } row && part.HasText == row.Text) return Setting(row);
        if (part.M == "css" && action.Locator?.Kind == "nameValue" && action.Value is { Text: { } value } radio
            && part.Css is { } css && css.EndsWith(LocatorPolicy.CssValue(value), StringComparison.Ordinal))
            return $"$\"{CSharp.Escape(css[..^LocatorPolicy.CssValue(value).Length], true)}[value='{{CssText({Setting(radio)})}}']\"";
        if (part.M == "filter" && part.HasText is { } shown) return Linked(shown);
        if (part.M == "text" && part.Text is { } label) return Linked(label);
        if (part.M != "role" || part.Name is null) return null;
        if (action.Source.Widget == "radio" && action.Value is { } choice && part.Role == "radio" && part.Name == choice.Text)
            return Setting(choice);
        if (action.Member is { } member && _step.LoopValues.ContainsKey(member) && part.Name == member.Text) return Setting(member);
        if (action.RowKey is { Text: { } key } rowKey && part == action.Locator!.Parts[^1] && part.Name.Contains(key, StringComparison.Ordinal))
        {
            var at = part.Name.IndexOf(key, StringComparison.Ordinal);
            return $"$\"{CSharp.Escape(part.Name[..at], true)}{{{Setting(rowKey)}}}{CSharp.Escape(part.Name[(at + key.Length)..], true)}\"";
        }

        return Linked(LocatorPolicy.WithoutIcon(part.Name));
    }

    // The text with the values read so far in it read from their settings, as a check's expectation reads them; null
    // when it has none.
    private string? Seen(string text)
    {
        var pieces = Pieces(text);
        return pieces.Any(piece => piece.Code is not null) ? Code(pieces) : null;
    }

    // The text with the loop's item values in it read from the item, and the values typed before from their settings;
    // null when it has none.
    private string? Linked(string text)
    {
        var pieces = Pieces(text, ItemValues(), _typed);
        return pieces.Any(piece => piece.Code is not null) ? Code(pieces) : null;
    }

    // ── Values ───────────────────────────────────────────────────────────────────────────────────────────────────

    private string Setting(Setting? setting)
    {
        if (setting is null) return "\"\"";
        if (_step.LoopValues.TryGetValue(setting, out var item))
        {
            _step.ReadsItem = true;
            return item;
        }

        if (setting.DateFormat is not null) return DateOf(setting);
        return setting.Unique ? $"Unique({FromSettings(setting)})" : FromSettings(setting);
    }

    // A date relative to the day the flow runs, as the property of the flow that types it.
    private string DateOf(Setting setting)
    {
        _step.UsesFlow = true;
        if (_dates.TryGetValue(setting, out var member)) return member;
        var taken = new HashSet<string>(_dates.Values.Concat(CSharp.Reserved).Concat(["Configuration", "BaseUrl", "Context", "Item",
            "ItemIndex", "Unique", _plan.ClassName]), StringComparer.Ordinal);
        return _dates[setting] = Unique(RelativeDate.Member(setting.Name), taken);
    }

    private string FromSettings(Setting setting)
    {
        _step.UsesFlow = true;
        return $"Configuration.{setting.Name}";
    }

    // A value as the text a field is filled with: a number in the invariant culture, as YAML writes it.
    private string AsText(Setting? setting) =>
        setting is { IsNumber: true, DateFormat: null } ? $"{Setting(setting)}.ToString(CultureInfo.InvariantCulture)" : Setting(setting);

    // A text as pieces: what it says, and the values read so far in it with the C# that reads them.
    private List<(string Text, string? Code)> Pieces(string text) => Pieces(text, ItemValues(), _seen);

    // The loop's item values long enough to be told in a text, with the C# that reads each.
    private IEnumerable<(string Text, string Code)> ItemValues() =>
        _step.LoopValues.Where(value => value.Key.Text is { Length: >= 3 }).Select(value => (value.Key.Text!, value.Value))
            .Concat(_afterLoops.Select(value => (value.Key, value.Value)));

    private List<(string Text, string? Code)> Pieces(string text, IEnumerable<(string Text, string Code)> items,
        IEnumerable<Setting> settings)
    {
        var known = items.Select(item => (item.Text, Code: item.Code, Setting: (Setting?)null)).ToList();
        known.AddRange(settings
            .Where(setting => (setting.Type == "string" || setting.DateFormat is not null)
                              && setting.Text is { Length: >= 3 } value && value.Trim().Length > 0)
            .Select(setting => (setting.Text!, Code: "", Setting: (Setting?)setting)));
        var pieces = new List<(string Text, string? Code, Setting? Setting)> { (text, null, null) };
        foreach (var value in known.OrderByDescending(value => value.Text.Length))
            pieces = [.. pieces.SelectMany(piece => piece.Code is not null ? [piece] : Split(piece.Text, value))];
        return [.. pieces.Select(piece => (piece.Text, piece.Setting is { } setting ? Setting(setting) : piece.Code))];
    }

    private void MarkItemRead(string code)
    {
        if (_step.LoopValues.ContainsValue(code)) _step.ReadsItem = true;
        if (_afterLoops.ContainsValue(code)) _step.UsesFlow = true;
    }

    // The text around each place it shows the value, as a word of its own: "TradeCode 99" shows no "TradeCode 9", which
    // a case changing the setting would make "Alpha9" of.
    private static IEnumerable<(string, string?, Setting?)> Split(string text, (string Text, string Code, Setting? Setting) value)
    {
        var from = 0;
        foreach (var at in Generation.Text.WordsAt(text, value.Text))
        {
            if (at > from) yield return (text[from..at], null, null);
            yield return (value.Text, value.Code, value.Setting);
            from = at + value.Text.Length;
        }

        if (from < text.Length) yield return (text[from..], null, null);
    }

    // The pieces as C#: a literal, a setting alone, or an interpolated string.
    private string Code(List<(string Text, string? Code)> pieces)
    {
        foreach (var piece in pieces.Where(piece => piece.Code is not null)) MarkItemRead(piece.Code!);
        if (pieces.All(piece => piece.Code is null)) return CSharp.Literal(string.Concat(pieces.Select(piece => piece.Text)));
        if (pieces is [(_, { } alone)]) return alone;
        var code = new StringBuilder("$\"");
        foreach (var (text, value) in pieces)
            code.Append(value is null ? CSharp.Escape(text, interpolated: true) : $"{{{value}}}");
        return code.Append('"').ToString();
    }

    // The address a URL change went to, with a record's id in it matching any id. In an app that routes by the
    // fragment (#/orders/5), the route is the address; any other fragment (#top) is not checked.
    private static string UrlPattern(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return CSharp.Pattern(url);
        var path = PathPattern(uri.AbsolutePath);
        var hasQuery = uri.Query.Length > 1;
        var fragment = uri.Fragment.TrimStart('#');
        if (!fragment.StartsWith('/') && !fragment.StartsWith("!/", StringComparison.Ordinal))
            return path + (hasQuery ? @"\?" : fragment.Length > 0 ? "#" : "$");

        var routeQuery = fragment.IndexOf('?');
        var route = PathPattern(routeQuery < 0 ? fragment : fragment[..routeQuery]);
        return path + (hasQuery ? @"\?[^#]*" : "") + "#" + route + (routeQuery < 0 ? "$" : @"\?");
    }

    // A segment that is a record's id matches any; another matches as the page's address has it, percent-encoded as
    // a browser shows a segment not in ASCII, or as typed. Told apart by what it says, decoded: an encoded segment
    // holds digits ("%D7%94") that are no id.
    private static string PathPattern(string path) => string.Join('/', path.Split('/').Select(segment =>
        Uri.UnescapeDataString(segment) is var said && (said.Any(char.IsDigit) || (said.Length > 12 && UnstableId.IsGuidLike(said)))
            ? "[^/]+"
            : said == segment ? CSharp.Pattern(segment) : $"(?:{CSharp.Pattern(segment)}|{CSharp.Pattern(said)})"));


    // Relative to BaseUrl, which the probe opens first, so the flow runs against every environment.
    private string Address(string url)
    {
        if (url.StartsWith(_plan.StartUrl, StringComparison.Ordinal))
        {
            _step.UsesFlow = true;
            var rest = url[_plan.StartUrl.Length..];
            return rest.Length == 0 ? "BaseUrl" : $"$\"{{BaseUrl}}{CSharp.Escape(rest, interpolated: true)}\"";
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var target) && Uri.TryCreate(_plan.StartUrl, UriKind.Absolute, out var start)
            && target.GetLeftPart(UriPartial.Authority) == start.GetLeftPart(UriPartial.Authority))
        {
            _step.UsesFlow = true;
            return $"new Uri(new Uri(BaseUrl), {CSharp.Literal(target.PathAndQuery + target.Fragment)}).AbsoluteUri";
        }

        return CSharp.Literal(url);
    }

    private static string Files(IReadOnlyList<string> files) => files switch
    {
        [] => "Array.Empty<string>()",
        [var file] => $"FileToUpload({CSharp.Literal(file)})",
        _ => $"new[] {{ {string.Join(", ", files.Select(file => $"FileToUpload({CSharp.Literal(file)})"))} }}",
    };

    private static string? Join(string? first, string? second) =>
        first is null ? second : second is null ? first : $"{first}; {second}";

    private static string Unique(string name, HashSet<string> taken)
    {
        var unique = name;
        for (var copy = 2; !taken.Add(unique); copy++) unique = $"{name}{copy}";
        return unique;
    }

    // ── The file ─────────────────────────────────────────────────────────────────────────────────────────────────

    private GeneratedFlow Assemble(string namespaceName)
    {
        var className = _plan.ClassName;
        var configName = $"{className}Config";
        var code = new StringBuilder();
        var checks = new List<CheckByHand>();
        var actionLines = new Dictionary<string, LineSpan>();
        var stepLines = new Dictionary<string, LineSpan>();
        var statements = new List<ActionStatement>();
        var lineNumber = 0;
        void Append(string text, string? check = null, IReadOnlyList<string>? actions = null)
        {
            code.Append(text).Append('\n');
            lineNumber++;
            if (check is not null) checks.Add(new CheckByHand(lineNumber, check));
            foreach (var action in actions ?? [])
                actionLines[action] = actionLines.TryGetValue(action, out var span) ? span with { Last = lineNumber }
                    : new LineSpan(lineNumber, lineNumber);
        }

        void Statement(Line line, int indent)
        {
            var wrapped = Wrap(line.Text, indent + line.Depth * 4).ToList();
            var first = lineNumber + 1;
            for (var index = 0; index < wrapped.Count; index++)
            {
                var mark = index == wrapped.Count - 1 ? line.Mark : "";
                Append(wrapped[index] + mark, index == 0 ? line.Check : null, line.Actions);
            }

            if (line.Actions.Count > 0) statements.Add(new ActionStatement(new LineSpan(first, lineNumber), line.Actions));
        }

        var dates = DateMembers().ToList();
        var all = _run.Concat(_methods.SelectMany(method => method.Body)).Select(line => line.Text).Concat(dates).ToList();
        var usesUpload = all.Any(text => text.Contains("FileToUpload(", StringComparison.Ordinal));
        var usesCss = all.Any(text => text.Contains("CssText(", StringComparison.Ordinal));
        if (all.Any(text => text.Contains("CultureInfo.", StringComparison.Ordinal))) Append("using System.Globalization;");
        if (all.Any(text => text.Contains("Regex(", StringComparison.Ordinal))) Append("using System.Text.RegularExpressions;");
        Append("using Microsoft.Playwright;");
        Append("using QaaS.Playwright;");
        if (all.Any(text => text.Contains("Expect(", StringComparison.Ordinal))) Append("using static Microsoft.Playwright.Assertions;");
        Append("");
        Append($"namespace {namespaceName};");
        Append("");
        Append("/// <summary>");
        var recorded = _plan.After is { } after
            ? $"Recorded on {CSharp.Doc(_plan.StartUrl)} after <c>{after}</c>. Run it in a PlaywrightFlowProbe after that flow, " +
              $"with <c>SetupFlows: [{after}]</c> and <c>Flows: [{className}]</c>;"
            : $"Recorded on {CSharp.Doc(_plan.StartUrl)}. Run it in a PlaywrightFlowProbe with <c>Flows: [{className}]</c>;";
        var settings = $"each case sets its values under <c>FlowConfiguration:{className}</c>.";
        foreach (var text in WordWrap($"{recorded} {settings}", "/// ")) Append(text);
        Append("/// </summary>");
        Append($"public sealed class {className} : BasePlaywrightFlow<{configName}>");
        Append("{");
        Append("    public override async Task RunAsync(IPage page)");
        Append("    {");
        foreach (var line in _run) Statement(line, 8);
        Append("    }");
        foreach (var (step, signature, body) in _methods)
        {
            Append("");
            var summary = $"/// <summary>{CSharp.Doc(step.Summary)}</summary>";
            if (4 + summary.Length <= MaxWidth) Append($"    {summary}");
            else
            {
                Append("    /// <summary>");
                foreach (var text in WordWrap(CSharp.Doc(step.Summary), "    /// ")) Append(text);
                Append("    /// </summary>");
            }

            var first = lineNumber + 1;
            Append($"    {signature}");
            Append("    {");
            foreach (var line in body) Statement(line, 8);
            Append("    }");
            stepLines[step.Name] = new LineSpan(first, lineNumber);
        }

        foreach (var line in dates) Append(line);
        if (_acceptsDialogs.Count > 0)
        {
            Append("");
            Append("    // A browser dialog (an alert, a confirm) the flow accepts, as you did.");
            Append($"    private static void {AcceptBrowserDialog}(object? sender, IDialog dialog) => _ = dialog.AcceptAsync();");
        }

        if (usesUpload)
        {
            Append("");
            foreach (var line in FileToUploadMethod.Split('\n')) Append(line);
        }

        if (usesCss)
        {
            Append("");
            foreach (var line in CssTextMethod.Split('\n')) Append(line);
        }

        Append("}");
        Append("");
        foreach (var line in ConfigRecord(className, configName)) Append(line);

        foreach (var missing in _plan.NotRecorded) checks.Add(new CheckByHand(null, missing));
        return new GeneratedFlow(code.ToString(), FlowYaml.Write(className, _plan.Settings), checks)
        {
            Values = FlowYaml.Write(className, _plan.Settings, secrets: true),
            Secrets = [.. _plan.Settings.Where(setting => setting.Secret)], Suggestions = _plan.Suggestions,
            ActionLines = actionLines, StepLines = stepLines, Statements = statements,
            Expects = _expects.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<Expectation>)pair.Value),
        };
    }

    // Each date the flow types relative to the day it runs: as many days from today as its setting says, in the format
    // it was typed in.
    private IEnumerable<string> DateMembers()
    {
        foreach (var (setting, member) in _dates)
        {
            yield return "";
            var summary = $"The date <see cref=\"{_plan.ClassName}Config.{setting.Name}\"/> days from today, as it was typed.";
            if (8 + summary.Length + 19 <= MaxWidth) yield return $"    /// <summary>{summary}</summary>";
            else
            {
                yield return "    /// <summary>";
                foreach (var text in WordWrap(summary, "    /// ")) yield return text;
                yield return "    /// </summary>";
            }

            yield return $"    private string {member} =>";
            yield return $"        DateTime.Today.AddDays(Configuration.{setting.Name}).ToString({CSharp.Literal(setting.DateFormat!)}, " +
                         "CultureInfo.InvariantCulture);";
        }
    }

    // The settings record, in the order of the steps; a long one in groups, each named after the step that reads it.
    private IEnumerable<string> ConfigRecord(string className, string configName)
    {
        yield return $"/// <summary>The values <see cref=\"{className}\"/> recorded; each case can set its own.</summary>";
        if (_plan.Settings.Count == 0)
        {
            yield return $"public sealed record {configName};";
            yield break;
        }

        yield return $"public sealed record {configName}";
        yield return "{";
        var groups = FlowPlanner.SettingsByStep(_plan).GroupBy(read => read.Step, read => read.Setting).ToList();
        var grouped = _plan.Settings.Count > 5 && groups.Count > 1;
        foreach (var group in groups)
        {
            if (grouped)
            {
                if (group.Key != groups[0].Key) yield return "";
                yield return $"    // {group.Key.Name}";
            }

            foreach (var setting in group)
                foreach (var line in Property(setting)) yield return line;
        }

        foreach (var list in _plan.Settings.Where(setting => setting.ItemType is not null))
        {
            yield return "";
            yield return $"    /// <summary>One of <see cref=\"{list.Name}\"/>.</summary>";
            yield return $"    public sealed record {list.ItemType}";
            yield return "    {";
            foreach (var field in list.Fields!)
            {
                var initial = field.Type == "string" ? " = \"\";" : "";
                yield return $"        public {field.Type} {field.Name} {{ get; init; }}{initial}";
            }
            yield return "    }";
        }

        yield return "}";
    }

    private static IEnumerable<string> Property(Setting setting)
    {
        var declaration = $"    public {setting.CSharpType} {setting.Name} {{ get; init; }} =";
        // A secret has no default in the code: each case gives it, as its YAML reads it from the environment.
        if (setting.Secret)
        {
            yield return $"    /// <summary>A secret: no default here; the YAML reads it from {FlowYaml.Placeholder(setting)}.</summary>";
            yield return $"{declaration} \"\";";
            yield break;
        }

        if (setting.Items is not { } items)
        {
            if (setting is { Format: { } format, DateFormat: null })
                yield return $"    /// <summary>As the field takes it, whatever the browser shows: {format}.</summary>";
            yield return $"{declaration} {Default(setting, setting.Type)};";
            yield break;
        }

        var fields = setting.Fields!;
        string Field(Setting value, int index) => $"{fields[index].Name} = {Default(value, fields[index].Type)}";
        var values = items.Select(item => setting.ItemType is null
            ? Default(item[0], fields[0].Type)
            : $"new() {{ {string.Join(", ", item.Select(Field))} }}").ToList();
        var inline = $"{declaration} [{string.Join(", ", values)}];";
        if (inline.Length <= MaxWidth)
        {
            yield return inline;
            yield break;
        }

        yield return declaration;
        yield return "    [";
        foreach (var value in values) yield return $"        {value},";
        yield return "    ];";
    }

    private static string Default(Setting setting, string type) => type switch
    {
        "bool" => CSharp.Bool(setting.Flag == true),
        "int" => setting.Value ?? "0",
        "decimal" when setting.Value?.Contains('.', StringComparison.Ordinal) == true => setting.Value + "m",
        "decimal" => setting.Value ?? "0",
        _ => CSharp.Literal(setting.Value ?? ""),
    };

    // The statement on lines of at most MaxWidth, broken where it may break, each next line indented once more.
    private static IEnumerable<string> Wrap(string text, int indent)
    {
        var pad = new string(' ', indent);
        var segments = text.Split(Break);
        if (indent + segments.Sum(segment => segment.Length) <= MaxWidth)
        {
            yield return pad + string.Concat(segments);
            yield break;
        }

        var line = new StringBuilder(pad).Append(segments[0]);
        foreach (var segment in segments.Skip(1))
        {
            if (line.Length + segment.Length <= MaxWidth)
            {
                line.Append(segment);
                continue;
            }

            yield return line.ToString();
            line.Clear().Append(pad).Append("    ").Append(segment);
        }

        yield return line.ToString();
    }

    // The text on lines of at most MaxWidth, broken between words, never inside <c>...</c>.
    private static IEnumerable<string> WordWrap(string text, string prefix)
    {
        var line = new StringBuilder(prefix);
        foreach (var word in DocWord().Matches(text).Select(match => match.Value))
        {
            if (line.Length > prefix.Length && line.Length + 1 + word.Length > MaxWidth)
            {
                yield return line.ToString();
                line.Clear().Append(prefix);
            }

            if (line.Length > prefix.Length) line.Append(' ');
            line.Append(word);
        }

        yield return line.ToString();
    }

    [GeneratedRegex(@"^\s*(loading|please wait|saving|processing|working|uploading|submitting|fetching|refreshing)\b[^.!?]{0,30}(\.\.\.|…)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Passing();

    // The button a message ends with, its words in capitals as MUI writes them (UNDO, DISMISS), or a cross.
    [GeneratedRegex(@"(?<=\S)\s+(UNDO|DISMISS|CLOSE|RETRY|OK|VIEW|SHOW|[×✕✖])$")]
    private static partial Regex AlertButton();

    [GeneratedRegex(@"\S*\d\S*")]
    private static partial Regex NumberWord();

    [GeneratedRegex("[A-Z][a-z0-9]*$")]
    private static partial Regex LastWord();

    [GeneratedRegex(@"\S*<c>.*?</c>\S*|\S+")]
    private static partial Regex DocWord();
}
