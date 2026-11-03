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

/// <summary>The flow's source, its settings as YAML, and what to check by hand.</summary>
internal sealed record GeneratedFlow(string Code, string Yaml, IReadOnlyList<CheckByHand> Checks)
{
    /// <summary>
    /// The lines written for each recorded action, by the action's id. An action a loop repeats has the lines of the
    /// loop's body that do it, as every repeat of it has.
    /// </summary>
    public IReadOnlyDictionary<string, LineSpan> ActionLines { get; init; } = new Dictionary<string, LineSpan>();

    /// <summary>The lines of each step's method, by the method's name.</summary>
    public IReadOnlyDictionary<string, LineSpan> StepLines { get; init; } = new Dictionary<string, LineSpan>();

    /// <summary>Each statement written for recorded actions, in the order of the code.</summary>
    public IReadOnlyList<ActionStatement> Statements { get; init; } = [];
}

/// <summary>
/// Writes a <see cref="FlowPlan"/> as a flow class: <c>RunAsync</c> calls one method per step, each step acts on the
/// page or dialog it is given and ends with the Expect of what it changed, and every recorded value is read from the
/// flow's settings. A locator used twice is a local of its own; a loop step does its first repeat for each item of its
/// list. Each Expect the recorder adds as a check is marked <c>// auto check</c>, and left out without auto checks; a
/// wait for the page to stop loading stays.
/// </summary>
internal sealed partial class FlowWriter
{
    /// <summary>The longest line of the flow's code, as in this repository's own; a mark of an auto check may pass it.</summary>
    public const int MaxWidth = 120;

    // Where a statement may break onto the next line, indented once more.
    private const string Break = "\u001F";
    /// <summary>What ends the line of a check the recorder added itself.</summary>
    public const string AutoCheck = " // auto check";
    private const string SlowTimeout = "new() { Timeout = 30_000 }";

    private const string FileToUploadMethod = """
            // The recorder saw only the names of the files you uploaded: return each one's full path.
            private static string FileToUpload(string recordedName) =>
                throw new NotImplementedException($"Return the full path of the file recorded as '{recordedName}'.");
        """;

    private readonly FlowPlan _plan;
    private readonly bool _autoChecks;
    private readonly List<Line> _run = [];
    private readonly List<(Step Step, string Signature, List<Line> Body)> _methods = [];
    private readonly Dictionary<string, string> _dialogVariables = [];
    private readonly HashSet<string> _runVariables = ["page"];

    // The step being written; each step is written twice, first to count how often each locator is used.
    private StepState _step = null!;

    private FlowWriter(FlowPlan plan, bool autoChecks) => (_plan, _autoChecks) = (plan, autoChecks);

    /// <param name="plan">The flow to write.</param>
    /// <param name="namespaceName">The flow's namespace.</param>
    /// <param name="autoChecks">Whether to check after each action what it did when recorded; waits stay without.</param>
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

        public bool Auto { get; init; }
        public int Depth { get; init; }
    }

    // Where a step's actions run: its dialog, whose page is dialog.Page, or a page.
    private sealed record Scope(string Root, string Page, string? Dialog);

    private sealed class StepState(Scope scope, StepState? counted)
    {
        public Scope Scope { get; } = scope;

        /// <summary>How often each locator is used, as counted by the first writing; null while counting.</summary>
        public Dictionary<string, int>? Uses { get; } = counted?.Counted;

        /// <summary>The locators found in each row, as counted by the first writing; null while counting.</summary>
        public Dictionary<string, HashSet<string>>? RowUses { get; } = counted?.RowsCounted;

        public Dictionary<string, int> Counted { get; } = [];
        public Dictionary<string, HashSet<string>> RowsCounted { get; } = [];
        public HashSet<string> Locals { get; } = new(StringComparer.Ordinal) { scope.Root, "page" };
        public Dictionary<string, string> Declared { get; } = [];
        public List<Line> Lines { get; } = [];

        /// <summary>Lines to write before the loop: the locals its body uses that its items do not change.</summary>
        public List<Line> BeforeLoop { get; } = [];

        /// <summary>The C# of each value a loop's item holds, e.g. <c>line.Quantity</c>, by the first repeat's setting.</summary>
        public Dictionary<Setting, string> LoopValues { get; } = [];

        public IReadOnlyList<string> Actions { get; set; } = [];

        /// <summary>In a loop: each repeat's actions, and the place in them of the action being written.</summary>
        public (List<PlannedAction[]> All, int Index)? Repeats { get; set; }

        public int Depth { get; set; }
        public bool UsesFlow { get; set; }

        /// <summary>The C# written since it was reset reads a loop's item.</summary>
        public bool ReadsItem { get; set; }

        public void Add(string text, string? check = null, bool auto = false) =>
            Lines.Add(new Line(text, check) { Actions = Actions, Auto = auto, Depth = Depth });
    }

    private void WriteStep(Step step)
    {
        var argument = step.Dialog is { } title ? DialogVariable(step, title) : step.Page;
        var scope = step.Dialog is not null ? new Scope("dialog", "dialog.Page", step.Dialog) : new Scope(step.Page, step.Page, null);

        _step = new StepState(scope, counted: null);
        WriteBody(step, last: false);
        _step = new StepState(scope, _step);
        WriteBody(step, last: true);

        var returns = step.Opens is not null ? "Task<ILocator>" : step.OpensPage is not null ? "Task<IPage>" : "Task";
        var parameter = step.Dialog is not null ? "ILocator dialog" : $"IPage {step.Page}";
        _methods.Add((step, $"private {(_step.UsesFlow ? "" : "static ")}async {returns} {step.Name}({parameter})", _step.Lines));

        // What a step opens is kept when a later step works in it.
        var call = $"{step.Name}({argument})";
        var later = _plan.Steps.Skip(_plan.Steps.IndexOf(step) + 1).ToList();
        if (step.Opens is { } opened && later.Any(other => other.Dialog == opened.Title))
        {
            var variable = NewDialogVariable(step);
            _dialogVariables[opened.Title] = variable;
            _run.Add(new Line($"var {variable} = await {call};"));
        }
        else if (step.OpensPage is { } page && later.Any(other => other.Page == page))
            _run.Add(new Line($"var {page} = await {call};"));
        else
            _run.Add(new Line($"await {call};"));
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
            WriteAction(action, step, index == step.Actions.Count - 1, last);
        }
    }

    // A dialog that was open before the recording, or opened unseen, is declared where the flow first needs it.
    private string DialogVariable(Step step, string title)
    {
        if (_dialogVariables.TryGetValue(title, out var variable)) return variable;
        variable = Unique("dialog", _runVariables);
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
        return Unique(opensMany && subject.Length > 0 ? CSharp.Camel(subject) + "Dialog" : "dialog", _runVariables);
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
            var item = list.ItemType is { } type ? CSharp.Camel(type) : LoopVariable(list.Fields![0].Name);
            item = Unique(item, _step.Locals);
            var values = template.SelectMany(action => action.Values).ToList();
            for (var index = 0; index < values.Count; index++)
                _step.LoopValues[values[index]] = list.ItemType is null ? item : $"{item}.{list.Fields![index].Name}";
            foreach (var action in template.Where(action => LoopFinder.RowFromValue(action, template)))
                _step.LoopValues[action.RowKey!] = _step.LoopValues.First(value => value.Key.Text == action.RowKey!.Text).Value;
            header = $"foreach (var {item} in {FromSettings(list)})";
        }
        else
        {
            var counter = Unique("i", _step.Locals);
            header = $"for (var {counter} = 0; {counter} < {FromSettings(loop.Count!)}; {counter}++)";
        }

        var bodyStart = _step.Lines.Count;
        _step.Depth = 1;
        for (var index = 0; index < template.Length; index++)
        {
            _step.Actions = [.. repeats.Select(repeat => repeat[index].Source.Id)];
            _step.Repeats = (repeats, index);
            WriteAction(template[index], step, isLast: false, last);
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
        foreach (var (index, alert) in AlertsEveryRepeatShowedAlike(repeats).Where(_ => _autoChecks))
        {
            _step.Actions = [repeats[^1][index].Source.Id];
            AddCheck(AlertExpectation(alert, _step.Scope, many: alert.AlertCount > 1));
        }
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

    // ── Actions ──────────────────────────────────────────────────────────────────────────────────────────────────

    private void WriteAction(PlannedAction action, Step step, bool isLast, bool last)
    {
        var source = action.Source;
        var scope = _step.Scope;
        if (action.Has("jsDialog"))
            _step.Add($"{scope.Page}.Dialog += (_, browserDialog) => browserDialog.AcceptAsync();",
                "a browser dialog (alert or confirm) showed here: the flow accepts it; dismiss it where you did");

        var problem = source.HoverMenu == true
            ? "the menu it is in opened on hover: hover what opens it first (HoverAsync)"
            : null;
        switch (action.Kind)
        {
            case "click" or "dblclick":
            {
                var method = action.Kind == "click" ? "ClickAsync" : "DblClickAsync";
                if (action.Effects.FirstOrDefault(effect => effect.Kind == "popup") is { Page: { } popup })
                {
                    var locator = Locate(action, ref problem);
                    _step.Add($"var {popup} = await {scope.Page}.RunAndWaitForPopupAsync(() => {locator}.{method}());",
                        Join(problem, "a pop-up opened here: check what the flow does in it"));
                }
                else
                {
                    _step.Add($"await {Locate(action, ref problem)}{Break}.{method}();", problem);
                    // In a loop, a check stays only when it reads the loop's item.
                    if (_autoChecks && _step.Depth == 0 && action.Has("tabSelected"))
                        AddCheck($"await Expect({Locate(action, ref problem)}){Break}" +
                                 ".ToHaveAttributeAsync(\"aria-selected\", \"true\");");
                }

                break;
            }
            case "fill":
                _step.Add($"await {Locate(action, ref problem)}{Break}.FillAsync({Text(action.Value)});", problem);
                break;
            case "check" or "uncheck" when source.Widget == "radio":
                _step.Add($"await {Locate(action, ref problem)}{Break}.CheckAsync();", problem);
                break;
            case "check" or "uncheck":
                _step.Add($"await {Locate(action, ref problem)}{Break}.SetCheckedAsync({Setting(action.Value)});", problem);
                break;
            case "choose":
                WriteChoice(action, problem);
                break;
            case "press":
                if (source.OnPage == true || action.Locator is null)
                    _step.Add($"await {scope.Page}.Keyboard.PressAsync({CSharp.Literal(source.Key ?? "Enter")});");
                else
                {
                    var locator = Locate(action, ref problem);
                    _step.Add($"await {locator}{Break}.PressAsync({CSharp.Literal(source.Key ?? "Enter")});", problem);
                }

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
                _step.Add($"var {source.Page} = await {scope.Page}.Context.NewPageAsync();",
                    source.AfterPause == true
                        ? "a tab that opened while the recording was paused: the flow opens it here"
                        : "a tab you opened yourself: the flow opens it here");
                if (source.Url is { } opened) _step.Add($"await {source.Page}.GotoAsync({Address(opened)});");
                break;
            default:
                if (last)
                    _plan.NotRecorded.Add($"a '{action.Kind}' the recorder did not understand, in {step.Name}: redo it by hand");
                break;
        }

        WriteEffects(action, step, isLast);
    }

    private void WriteChoice(PlannedAction action, string? problem)
    {
        var source = action.Source;
        var scope = _step.Scope;
        var option = $"{scope.Page}.GetByRole(AriaRole.Option, new() {{ Name = {Setting(action.OptionValue ?? action.Value)}, Exact = true }})";
        var optionProblem = source.Option is null || source.Option.Candidates.Any(candidate =>
            candidate is { Kind: "role", Matches: true } && candidate.Parts[^1] is { Exact: true } part && part.Name == source.Value)
            ? null
            : "the option's name did not find it alone when recorded";
        switch (source.Widget)
        {
            case "select" or "autocomplete":
                _step.Add(source.Widget == "autocomplete" && action.SearchValue is not null
                    ? $"await {Locate(action, ref problem)}{Break}.FillAsync({Setting(action.SearchValue)});"
                    : $"await {Locate(action, ref problem)}{Break}.ClickAsync();", problem);
                _step.Add($"await {option}{Break}.ClickAsync();", optionProblem);
                // In a loop, a check stays only when it reads the loop's item.
                if (_autoChecks && (_step.Depth == 0 || _step.LoopValues.ContainsKey(action.ShownValue ?? action.Value!)))
                {
                    var field = Locate(action, ref problem);
                    AddCheck(source.Widget == "select"
                        ? $"await Expect({field}){Break}.ToHaveTextAsync({Setting(action.ShownValue ?? action.Value)});"
                        : $"await Expect({field}){Break}.ToHaveValueAsync({Setting(action.Value)});");
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
                _step.Add($"await {option}{Break}.ClickAsync();", optionProblem);
                break;
        }
    }

    // What the action changed: the dialog it closed, the address it went to (unless a stronger check says the same),
    // the page's wait, the message it showed; and for the step's last action, the dialog it opened.
    private void WriteEffects(PlannedAction action, Step step, bool isLast)
    {
        var scope = _step.Scope;
        var repeats = _step.Repeats;
        var closed = action.Effects.Where(effect => effect.Kind == "dialogClosed").DistinctBy(effect => effect.Dialog?.Title)
            .ToList();
        foreach (var dialog in closed.Where(_ => _autoChecks))
        {
            var locator = dialog.Dialog?.Title == scope.Dialog && scope.Dialog is not null
                ? "dialog"
                : DialogLocator(scope.Page, dialog.Dialog).Locator;
            AddCheck($"await Expect({locator}){Break}.ToBeHiddenAsync({SlowTimeout});");
        }

        var alerts = action.Effects.Where(IsAlert).DistinctBy(effect => effect.Text).ToList();
        var shownElsewhere = action.Has("dialogOpened") || action.Has("tabSelected") || (closed.Count > 0 && alerts.Count > 0);
        var changes = action.Effects.Where(effect => _autoChecks && effect.Kind == "urlChanged");
        foreach (var changed in changes.TakeLast(shownElsewhere ? 0 : 1))
        {
            var page = changed.Page is { } alias && alias != action.Source.Page ? alias : scope.Page;
            AddCheck($"await Expect({page}){Break}.ToHaveURLAsync(new Regex({CSharp.Literal(UrlPattern(changed.Url!))}));");
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
            AddCheck(AlertExpectation(alert, scope));

        if (isLast && step.Opens is { } opened)
        {
            var (locator, check) = DialogLocator(scope.Page, opened);
            if (!_autoChecks)
            {
                _step.Add($"return {locator};", check);
                return;
            }

            var variable = Unique(scope.Dialog is null ? "dialog" : "opened", _step.Locals);
            _step.Add($"var {variable} = {locator};", check);
            AddCheck($"await Expect({variable}){Break}.ToBeVisibleAsync();");
            _step.Add($"return {variable};");
        }
        else if (isLast && step.OpensPage is { } page)
            _step.Add($"return {page};");
    }

    private void AddCheck(string text) => _step.Add(text, auto: true);

    private static bool IsAlert(CapturedEffect effect) => effect is { Kind: "alert", Text.Length: > 0 };

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
        var pieces = Pieces(alert.Text!);
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
        var pieces = Pieces(dialog.Title);
        var hasNumber = pieces.Where(piece => piece.Code is null).Any(piece => piece.Text.Any(char.IsDigit));
        if (dialog.TitleMatches && !hasNumber) return ($"{page}.{role}, new() {{ Name = {Code(pieces)} }})", null);
        if (dialog is { PrefixMatches: true, Prefix: { } prefix })
            return ($"{page}.{role}, new() {{ Name = {CSharp.Literal(prefix)} }})", null);
        if (dialog.TitleMatches) return ($"{page}.{role}, new() {{ Name = {CSharp.Literal(dialog.Title)} }})", null);
        if (dialog is { RawMatches: true, Raw: { } raw })
            return ($"{page}.{role}, new() {{ Name = {CSharp.Literal(raw)}, Exact = true }})", null);
        if (dialog.UnnamedMatches) return ($"{page}.{role})", null);
        return ($"{page}.{role}, new() {{ Name = {CSharp.Literal(dialog.Title)} }})",
            "this dialog's title did not find it alone when recorded");
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
            var header = CSharp.Pascal(action.Source.Target?.Row?.Header) is { } column ? CSharp.Camel(column) + "Row" : "row";
            expression = LocatorPolicy.Render(Declare(row.Row, header, null, changesWithItem), row.InIt, NameOf, Break);
        }

        problem = Join(why, problem);
        return expression;
    }

    private string Declare(string expression, string name, string? why, bool changesWithItem)
    {
        if (_step.Declared.TryGetValue(expression, out var local)) return local;
        local = Unique(_step.Locals.Contains(name) ? name + "Field" : name, _step.Locals);
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
        if (candidate.Kind == "generator") return LocatorPolicy.RenderGenerated(_step.Scope.Page, candidate);
        var (root, parts) = Rooted(candidate, source);
        return LocatorPolicy.Render(root, parts, nameOf, Break);
    }

    // Found in the step's own dialog: from the dialog the step is given.
    private (string Root, List<LocatorPart> Parts) Rooted(CapturedCandidate candidate, CapturedAction source)
    {
        var scope = _step.Scope;
        var parts = candidate.Parts;
        return parts is [{ M: "role", Role: "dialog" }, ..] && scope.Dialog is not null && source.Dialog?.Title == scope.Dialog
            ? (scope.Root, parts[1..])
            : (scope.Page, parts);
    }

    // A radio is found by the option's name or value and a row by its key, all read from the settings; in a loop, a name
    // with the item's value in it reads the item.
    private string? NameFromSettings(PlannedAction action, LocatorPart part)
    {
        if (part.M == "filter" && action.RowKey is { } row && part.HasText == row.Text) return Setting(row);
        if (part.M == "css" && action.Locator?.Kind == "nameValue" && action.Value is { Text: { } value } radio
            && part.Css is { } css && css.EndsWith(LocatorPolicy.CssValue(value), StringComparison.Ordinal))
            return $"$\"{CSharp.Escape(css[..^LocatorPolicy.CssValue(value).Length], true)}[value='{{{Setting(radio)}}}']\"";
        if (part.M == "filter" && part.HasText is { } shown && _step.LoopValues.Count > 0) return ItemIn(shown);
        if (part.M != "role" || part.Name is null) return null;
        if (action.Source.Widget == "radio" && action.Value is { } choice && part.Role == "radio" && part.Name == choice.Text)
            return Setting(choice);
        if (action.RowKey is { Text: { } key } rowKey && part == action.Locator!.Parts[^1] && part.Name.Contains(key, StringComparison.Ordinal))
        {
            var at = part.Name.IndexOf(key, StringComparison.Ordinal);
            return $"$\"{CSharp.Escape(part.Name[..at], true)}{{{Setting(rowKey)}}}{CSharp.Escape(part.Name[(at + key.Length)..], true)}\"";
        }

        return _step.LoopValues.Count > 0 ? ItemIn(part.Name) : null;
    }

    // The text with the loop's item values in it read from the item, or null when it has none.
    private string? ItemIn(string text)
    {
        var pieces = Pieces(text, ItemValues(), settings: false);
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

        return FromSettings(setting);
    }

    private string FromSettings(Setting setting)
    {
        _step.UsesFlow = true;
        return $"Configuration.{setting.Name}";
    }

    // A value as the text a field is filled with: a number in the invariant culture, as YAML writes it.
    private string Text(Setting? setting) =>
        setting?.IsNumber == true ? $"{Setting(setting)}.ToString(CultureInfo.InvariantCulture)" : Setting(setting);

    // A text as pieces: what it says, and the recorded values in it with the C# that reads them.
    private List<(string Text, string? Code)> Pieces(string text) => Pieces(text, ItemValues(), settings: true);

    // The loop's item values long enough to be told in a text, with the C# that reads each.
    private IEnumerable<(string Text, string Code)> ItemValues() =>
        _step.LoopValues.Where(value => value.Key.Text is { Length: >= 3 }).Select(value => (value.Key.Text!, value.Value));

    private List<(string Text, string? Code)> Pieces(string text, IEnumerable<(string Text, string Code)> items, bool settings)
    {
        var known = items.Select(item => (item.Text, Code: item.Code, Setting: (Setting?)null)).ToList();
        if (settings)
            known.AddRange(_plan.Settings
                .Where(setting => setting is { Type: "string", Text: { Length: >= 3 } value } && value.Trim().Length > 0)
                .Select(setting => (setting.Text!, Code: "", Setting: (Setting?)setting)));
        var pieces = new List<(string Text, string? Code, Setting? Setting)> { (text, null, null) };
        foreach (var value in known.OrderByDescending(value => value.Text.Length))
            pieces = [.. pieces.SelectMany(piece => piece.Code is not null ? [piece] : Split(piece.Text, value))];
        return [.. pieces.Select(piece => (piece.Text, piece.Setting is { } setting ? Setting(setting) : piece.Code))];
    }

    private void MarkItemRead(string code)
    {
        if (_step.LoopValues.ContainsValue(code)) _step.ReadsItem = true;
    }

    private static IEnumerable<(string, string?, Setting?)> Split(string text, (string Text, string Code, Setting? Setting) value)
    {
        var parts = text.Split(value.Text);
        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index].Length > 0) yield return (parts[index], null, null);
            if (index < parts.Length - 1) yield return (value.Text, value.Code, value.Setting);
        }
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
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return RegexEscape(url);
        var path = PathPattern(uri.AbsolutePath);
        var hasQuery = uri.Query.Length > 1;
        var fragment = uri.Fragment.TrimStart('#');
        if (!fragment.StartsWith('/') && !fragment.StartsWith("!/", StringComparison.Ordinal))
            return path + (hasQuery ? @"\?" : fragment.Length > 0 ? "#" : "$");

        var routeQuery = fragment.IndexOf('?');
        var route = PathPattern(routeQuery < 0 ? fragment : fragment[..routeQuery]);
        return path + (hasQuery ? @"\?[^#]*" : "") + "#" + route + (routeQuery < 0 ? "$" : @"\?");
    }

    private static string PathPattern(string path) => string.Join('/', path.Split('/').Select(segment =>
        segment.Any(char.IsDigit) || (segment.Length > 12 && UnstableId.IsGuidLike(segment))
            ? "[^/]+"
            : RegexEscape(Uri.UnescapeDataString(segment))));

    private static string RegexEscape(string text) => RegexSpecial().Replace(text, @"\$0");

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
                var mark = line.Auto && index == wrapped.Count - 1 ? AutoCheck : "";
                Append(wrapped[index] + mark, index == 0 ? line.Check : null, line.Actions);
            }

            if (line.Actions.Count > 0) statements.Add(new ActionStatement(new LineSpan(first, lineNumber), line.Actions));
        }

        var all = _run.Concat(_methods.SelectMany(method => method.Body)).Select(line => line.Text).ToList();
        var usesUpload = all.Any(text => text.Contains("FileToUpload(", StringComparison.Ordinal));
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
            ? $"Recorded on {XmlText(_plan.StartUrl)} after <c>{after}</c>. Run it in a PlaywrightFlowProbe after that flow, " +
              $"with <c>SetupFlows: [{after}]</c> and <c>Flows: [{className}]</c>;"
            : $"Recorded on {XmlText(_plan.StartUrl)}. Run it in a PlaywrightFlowProbe with <c>Flows: [{className}]</c>;";
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
            var summary = $"/// <summary>{XmlText(step.Summary)}</summary>";
            if (4 + summary.Length <= MaxWidth) Append($"    {summary}");
            else
            {
                Append("    /// <summary>");
                foreach (var text in WordWrap(XmlText(step.Summary), "    /// ")) Append(text);
                Append("    /// </summary>");
            }

            var first = lineNumber + 1;
            Append($"    {signature}");
            Append("    {");
            foreach (var line in body) Statement(line, 8);
            Append("    }");
            stepLines[step.Name] = new LineSpan(first, lineNumber);
        }

        if (usesUpload)
        {
            Append("");
            foreach (var line in FileToUploadMethod.Split('\n')) Append(line);
        }

        Append("}");
        Append("");
        foreach (var line in ConfigRecord(className, configName)) Append(line);

        foreach (var missing in _plan.NotRecorded) checks.Add(new CheckByHand(null, missing));
        return new GeneratedFlow(code.ToString(), FlowYaml.Write(className, _plan.Settings), checks)
        {
            ActionLines = actionLines, StepLines = stepLines, Statements = statements,
        };
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
        if (setting.Items is not { } items)
        {
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
        "int" => setting.Text ?? "0",
        "decimal" when setting.Text?.Contains('.', StringComparison.Ordinal) == true => setting.Text + "m",
        "decimal" => setting.Text ?? "0",
        _ => CSharp.Literal(setting.Text ?? ""),
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

    // A text in an XML doc comment.
    private static string XmlText(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    [GeneratedRegex(@"[\\^$.|?*+()\[\]{}]")]
    private static partial Regex RegexSpecial();

    [GeneratedRegex(@"\S*\d\S*")]
    private static partial Regex NumberWord();

    [GeneratedRegex("[A-Z][a-z0-9]*$")]
    private static partial Regex LastWord();

    [GeneratedRegex(@"\S*<c>.*?</c>\S*|\S+")]
    private static partial Regex DocWord();
}
