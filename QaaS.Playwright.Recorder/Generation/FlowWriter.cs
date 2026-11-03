using System.Text;
using System.Text.RegularExpressions;
using QaaS.Playwright.Recorder.Capture;

namespace QaaS.Playwright.Recorder.Generation;

/// <summary>A line of the flow a person should look at, and why.</summary>
internal sealed record CheckByHand(int? Line, string Message);

/// <summary>Lines of a flow's code, numbered from 1, both included.</summary>
internal sealed record LineSpan(int First, int Last);

/// <summary>The flow's source, its settings as YAML, and what to check by hand.</summary>
internal sealed record GeneratedFlow(string Code, string Yaml, IReadOnlyList<CheckByHand> Checks)
{
    /// <summary>The lines written for each recorded action, by the action's id.</summary>
    public IReadOnlyDictionary<string, LineSpan> ActionLines { get; init; } = new Dictionary<string, LineSpan>();

    /// <summary>The lines of each step's method, by the method's name.</summary>
    public IReadOnlyDictionary<string, LineSpan> StepLines { get; init; } = new Dictionary<string, LineSpan>();
}

/// <summary>
/// Writes a <see cref="FlowPlan"/> as a flow class: <c>RunAsync</c> calls one method per step, each step acts on the
/// page or dialog it is given and ends with the Expect of what it changed, and every recorded value is read from the
/// flow's settings.
/// </summary>
internal sealed partial class FlowWriter
{
    private const string FileToUploadMethod = """
            // The recorder saw only the names of the files you uploaded: return each one's full path.
            private static string FileToUpload(string recordedName) =>
                throw new NotImplementedException($"Return the full path of the file recorded as '{recordedName}'.");
        """;

    private readonly FlowPlan _plan;
    private readonly List<Line> _run = [];
    private readonly List<(string Step, string Signature, List<Line> Body)> _methods = [];
    private readonly Dictionary<string, string> _dialogVariables = [];
    private readonly HashSet<string> _runVariables = ["page"];

    // Whether the step being written reads the flow's own members (its settings, BaseUrl), so it cannot be static.
    private bool _stepUsesFlow;

    private FlowWriter(FlowPlan plan) => _plan = plan;

    public static GeneratedFlow Write(FlowPlan plan, string namespaceName)
    {
        var writer = new FlowWriter(plan);
        foreach (var step in plan.Steps) writer.WriteStep(step);
        return writer.Assemble(namespaceName);
    }

    private sealed record Line(string Text, string? Check = null)
    {
        /// <summary>The id of the recorded action the line is written for.</summary>
        public string? Action { get; init; }
    }

    // Where a step's actions run: its dialog, whose page is dialog.Page, or a page.
    private sealed record Scope(string Root, string Page, string? Dialog);

    private void WriteStep(Step step)
    {
        var argument = step.Dialog is { } title ? DialogVariable(step, title) : step.Page;
        var scope = step.Dialog is not null ? new Scope("dialog", "dialog.Page", step.Dialog) : new Scope(step.Page, step.Page, null);
        var locals = new HashSet<string>(StringComparer.Ordinal) { scope.Root, "page" };
        _stepUsesFlow = false;
        var body = new List<Line>();
        for (var index = 0; index < step.Actions.Count; index++)
        {
            var action = step.Actions[index];
            body.AddRange(WriteAction(action, scope, step, index == step.Actions.Count - 1, locals)
                .Select(line => line with { Action = action.Source.Id }));
        }

        var returns = step.Opens is not null ? "Task<ILocator>" : step.OpensPage is not null ? "Task<IPage>" : "Task";
        var parameter = step.Dialog is not null ? "ILocator dialog" : $"IPage {step.Page}";
        _methods.Add((step.Name, $"private {(_stepUsesFlow ? "" : "static ")}async {returns} {step.Name}({parameter})", body));

        var call = $"{step.Name}({argument})";
        if (step.Opens is { } opened)
        {
            var variable = NewDialogVariable(step);
            _dialogVariables[opened.Title] = variable;
            _run.Add(new Line($"var {variable} = await {call};"));
        }
        else if (step.OpensPage is { } page)
            _run.Add(new Line($"var {page} = await {call};"));
        else
            _run.Add(new Line($"await {call};"));
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

    private IEnumerable<Line> WriteAction(PlannedAction action, Scope scope, Step step, bool last, HashSet<string> locals)
    {
        var source = action.Source;
        if (action.Has("jsDialog"))
            yield return new Line($"{scope.Page}.Dialog += (_, browserDialog) => browserDialog.AcceptAsync();",
                "a browser dialog (alert or confirm) showed here: the flow accepts it; dismiss it where you did");

        var problem = LocatorPolicy.Problem(action.Locator);
        if (source.HoverMenu == true)
            problem = Join(problem, "the menu it is in opened on hover: hover what opens it first (HoverAsync)");

        switch (action.Kind)
        {
            case "click" or "dblclick":
            {
                var method = action.Kind == "click" ? "ClickAsync" : "DblClickAsync";
                var locator = Locate(action, scope);
                if (action.Effects.FirstOrDefault(effect => effect.Kind == "popup") is { Page: { } popup })
                    yield return new Line($"var {popup} = await {scope.Page}.RunAndWaitForPopupAsync(() => {locator}.{method}());",
                        Join(problem, "a pop-up opened here: check what the flow does in it"));
                else if (action.Has("tabSelected"))
                {
                    var tab = Unique(CSharp.IsAsciiText(source.Target?.Text) && CSharp.Pascal(source.Target!.Text) is { } name
                        ? CSharp.Camel(name) : "tab", locals);
                    yield return new Line($"var {tab} = {locator};", problem);
                    yield return new Line($"await {tab}.{method}();");
                    yield return new Line($"await Expect({tab}).ToHaveAttributeAsync(\"aria-selected\", \"true\");");
                }
                else
                    yield return new Line($"await {locator}.{method}();", problem);
                break;
            }
            case "fill":
                yield return new Line($"await {Locate(action, scope)}.FillAsync({Setting(action.Value)});", problem);
                break;
            case "check" or "uncheck" when source.Widget == "radio":
                yield return new Line($"await {Locate(action, scope)}.CheckAsync();", problem);
                break;
            case "check" or "uncheck":
                yield return new Line($"await {Locate(action, scope)}.SetCheckedAsync({Setting(action.Value)});", problem);
                break;
            case "choose":
                foreach (var line in WriteChoice(action, scope, locals, problem)) yield return line;
                break;
            case "press":
                yield return source.OnPage == true || action.Locator is null
                    ? new Line($"await {scope.Page}.Keyboard.PressAsync({CSharp.Literal(source.Key ?? "Enter")});")
                    : new Line($"await {Locate(action, scope)}.PressAsync({CSharp.Literal(source.Key ?? "Enter")});", problem);
                break;
            case "upload":
                yield return new Line($"await {Locate(action, scope)}.SetInputFilesAsync({Files(source.Files ?? [])});",
                    Join(problem, "upload: the recorder saw only the file names; return each one's full path in FileToUpload"));
                break;
            case "drag":
                yield return source.To is { } to && LocatorPolicy.Choose(to) is { } drop
                    ? new Line($"await {Locate(action, scope)}.DragToAsync({Render(drop, source, scope)});",
                        Join(problem, "drag and drop: check where it drops"))
                    : new Line($"await {Locate(action, scope)}.HoverAsync();",
                        Join(problem, "a drag the recorder could not follow: add DragToAsync() to where you dropped it"));
                break;
            case "navigate":
                yield return new Line($"await {scope.Page}.GotoAsync({Address(source.Url ?? _plan.StartUrl)});",
                    source.AfterPause == true
                        ? "the recording was paused, and the app was at this address when it resumed: the flow goes there " +
                          "by the address; check that it can, as what led there was not recorded"
                        : null);
                break;
            case "newTab":
                yield return new Line($"var {source.Page} = await {scope.Page}.Context.NewPageAsync();",
                    source.AfterPause == true
                        ? "a tab that opened while the recording was paused: the flow opens it here"
                        : "a tab you opened yourself: the flow opens it here");
                if (source.Url is { } opened) yield return new Line($"await {source.Page}.GotoAsync({Address(opened)});");
                break;
            default:
                _plan.NotRecorded.Add($"a '{action.Kind}' the recorder did not understand, in {step.Name}: redo it by hand");
                break;
        }

        foreach (var line in WriteEffects(action, scope, step, last, locals)) yield return line;
    }

    private IEnumerable<Line> WriteChoice(PlannedAction action, Scope scope, HashSet<string> locals, string? problem)
    {
        var source = action.Source;
        var option = $"{scope.Page}.GetByRole(AriaRole.Option, new() {{ Name = {Setting(action.OptionValue ?? action.Value)}, Exact = true }})";
        var optionProblem = source.Option is null || source.Option.Candidates.Any(candidate =>
            candidate is { Kind: "role", Matches: true } && candidate.Parts[^1] is { Exact: true } part && part.Name == source.Value)
            ? null
            : "the option's name did not find it alone when recorded";
        switch (source.Widget)
        {
            case "select" or "autocomplete":
            {
                var field = Unique(CSharp.Camel(action.Value!.Name), locals);
                yield return new Line($"var {field} = {Locate(action, scope)};", problem);
                yield return source.Widget == "autocomplete" && action.SearchValue is not null
                    ? new Line($"await {field}.FillAsync({Setting(action.SearchValue)});")
                    : new Line($"await {field}.ClickAsync();");
                yield return new Line($"await {option}.ClickAsync();", optionProblem);
                yield return source.Widget == "select"
                    ? new Line($"await Expect({field}).ToHaveTextAsync({Setting(action.ShownValue ?? action.Value)});")
                    : new Line($"await Expect({field}).ToHaveValueAsync({Setting(action.Value)});");
                break;
            }
            case "native":
                yield return new Line(
                    $"await {Locate(action, scope)}.SelectOptionAsync(new SelectOptionValue {{ Label = {Setting(action.Value)} }});", problem);
                break;
            default:
                yield return new Line($"await {option}.ClickAsync();", optionProblem);
                break;
        }
    }

    private IEnumerable<Line> WriteEffects(PlannedAction action, Scope scope, Step step, bool last, HashSet<string> locals)
    {
        foreach (var closed in action.Effects.Where(effect => effect.Kind == "dialogClosed")
                     .DistinctBy(effect => effect.Dialog?.Title))
        {
            var dialog = closed.Dialog?.Title == scope.Dialog && scope.Dialog is not null ? "dialog" : DialogLocator(scope.Page, closed.Dialog).Locator;
            yield return new Line($"await Expect({dialog}).ToBeHiddenAsync(new() {{ Timeout = 30000 }});");
        }

        foreach (var changed in action.Effects.Where(effect => effect.Kind == "urlChanged").TakeLast(1))
        {
            var page = changed.Page is { } alias && alias != action.Source.Page ? alias : scope.Page;
            yield return new Line($"await Expect({page}).ToHaveURLAsync(new Regex({CSharp.Literal(UrlPattern(changed.Url!))}));");
        }

        foreach (var alert in action.Effects.Where(effect => effect is { Kind: "alert", Text.Length: > 0 }).DistinctBy(effect => effect.Text))
            yield return AlertExpectation(alert, scope);

        if (last && step.Opens is { } opened)
        {
            var variable = Unique(scope.Dialog is null ? "dialog" : "opened", locals);
            var (locator, check) = DialogLocator(scope.Page, opened);
            yield return new Line($"var {variable} = {locator};", check);
            yield return new Line($"await Expect({variable}).ToBeVisibleAsync();");
            yield return new Line($"return {variable};");
        }
        else if (last && step.OpensPage is { } page)
            yield return new Line($"return {page};");
    }

    // An alert's text is checked when it says nothing that changes from case to case: the recorded values in it are
    // read from the settings, and a text that still has a number in it is not checked.
    private Line AlertExpectation(CapturedEffect alert, Scope scope)
    {
        var alerts = $"{scope.Page}.GetByRole(AriaRole.Alert)";
        var (text, hasNumber) = Interpolate(alert.Text!);
        if (!hasNumber) return new Line($"await Expect({alerts}.Filter(new() {{ HasText = {text} }})).ToBeVisibleAsync();");
        if (alert.AlertCount is null or <= 1) return new Line($"await Expect({alerts}).ToBeVisibleAsync();");
        var prefix = BeforeNumber(alert.Text!);
        return prefix.Length >= 3
            ? new Line($"await Expect({alerts}.Filter(new() {{ HasText = {CSharp.Literal(prefix)} }})).ToBeVisibleAsync();")
            : new Line($"await Expect({alerts}).Not.ToHaveCountAsync(0);");
    }

    // The dialog by its title. A title with a record's number in it is found by the words before the number, unless
    // the number is a recorded value.
    private (string Locator, string? Check) DialogLocator(string page, CapturedDialog? dialog)
    {
        const string role = "GetByRole(AriaRole.Dialog";
        if (dialog is null) return ($"{page}.{role})", "the recorder did not see this dialog's title");
        var (title, hasNumber) = Interpolate(dialog.Title);
        if (dialog.TitleMatches && !hasNumber) return ($"{page}.{role}, new() {{ Name = {title} }})", null);
        if (dialog is { PrefixMatches: true, Prefix: { } prefix })
            return ($"{page}.{role}, new() {{ Name = {CSharp.Literal(prefix)} }})", null);
        if (dialog.TitleMatches) return ($"{page}.{role}, new() {{ Name = {CSharp.Literal(dialog.Title)} }})", null);
        if (dialog is { RawMatches: true, Raw: { } raw })
            return ($"{page}.{role}, new() {{ Name = {CSharp.Literal(raw)}, Exact = true }})", null);
        if (dialog.UnnamedMatches) return ($"{page}.{role})", null);
        return ($"{page}.{role}, new() {{ Name = {CSharp.Literal(dialog.Title)} }})",
            "this dialog's title did not find it alone when recorded");
    }

    private string Locate(PlannedAction action, Scope scope)
    {
        if (action.Locator is null) return $"{scope.Page}.Locator(\"body\")";
        return Render(action.Locator, action.Source, scope, part => NameFromSettings(action, part));
    }

    private static string Render(CapturedCandidate candidate, CapturedAction source, Scope scope,
        Func<LocatorPart, string?>? nameOf = null)
    {
        if (candidate.Kind == "generator") return LocatorPolicy.RenderGenerated(scope.Page, candidate);
        var parts = candidate.Parts;
        // Found in the step's own dialog: from the dialog the step is given.
        if (parts is [{ M: "role", Role: "dialog" }, ..] && scope.Dialog is not null && source.Dialog?.Title == scope.Dialog)
            return LocatorPolicy.Render(scope.Root, parts.Skip(1), nameOf);
        return LocatorPolicy.Render(scope.Page, parts, nameOf);
    }

    // A radio is found by the option's name or value and a row by its key, all read from the settings.
    private string? NameFromSettings(PlannedAction action, LocatorPart part)
    {
        if (part.M == "filter" && action.RowKey is { } row && part.HasText == row.Text) return Setting(row);
        if (part.M == "css" && action.Locator?.Kind == "nameValue" && action.Value is { Text: { } value } radio
            && part.Css is { } css && css.EndsWith(CssValue(value), StringComparison.Ordinal))
            return $"$\"{CSharp.Escape(css[..^CssValue(value).Length], true)}[value='{{{Setting(radio)}}}']\"";
        if (part.M != "role" || part.Name is null) return null;
        if (action.Source.Widget == "radio" && action.Value is { } choice && part.Role == "radio" && part.Name == choice.Text)
            return Setting(choice);
        if (action.RowKey is { Text: { } key } rowKey && part == action.Locator!.Parts[^1] && part.Name.Contains(key, StringComparison.Ordinal))
        {
            var at = part.Name.IndexOf(key, StringComparison.Ordinal);
            return $"$\"{CSharp.Escape(part.Name[..at], true)}{{{Setting(rowKey)}}}{CSharp.Escape(part.Name[(at + key.Length)..], true)}\"";
        }

        return null;
    }

    private string Setting(Setting? setting)
    {
        if (setting is null) return "\"\"";
        _stepUsesFlow = true;
        return $"Configuration.{setting.Name}";
    }

    // An attribute's value as the capture script quotes it in CSS.
    private static string CssValue(string value) =>
        $"[value='{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)}']";

    // A text as C#, with the recorded values in it read from the settings; and whether a number is left in it.
    private (string Code, bool HasNumber) Interpolate(string text)
    {
        var values = _plan.Settings.Where(setting => setting.Text is { Length: >= 3 } value && value.Trim().Length > 0)
            .OrderByDescending(setting => setting.Text!.Length).ToList();
        var pieces = new List<(string Text, Setting? Setting)> { (text, null) };
        foreach (var setting in values)
            pieces = [.. pieces.SelectMany(piece => piece.Setting is not null ? [piece] : Split(piece.Text, setting))];

        var literal = string.Concat(pieces.Where(piece => piece.Setting is null).Select(piece => piece.Text));
        var hasNumber = literal.Any(char.IsDigit);
        if (pieces.All(piece => piece.Setting is null)) return (CSharp.Literal(text), hasNumber);
        var code = new StringBuilder("$\"");
        foreach (var (piece, setting) in pieces)
            code.Append(setting is null ? CSharp.Escape(piece, interpolated: true) : $"{{{Setting(setting)}}}");
        return (code.Append('"').ToString(), hasNumber);
    }

    private static IEnumerable<(string, Setting?)> Split(string text, Setting setting)
    {
        var parts = text.Split(setting.Text!);
        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index].Length > 0) yield return (parts[index], null);
            if (index < parts.Length - 1) yield return (setting.Text!, setting);
        }
    }

    private static string BeforeNumber(string text)
    {
        var words = text.Split(' ');
        var index = Array.FindIndex(words, word => word.Any(char.IsDigit));
        return (index < 0 ? text : string.Join(' ', words[..index])).TrimEnd(' ', '(', ':', '-', ',').Trim();
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
            _stepUsesFlow = true;
            var rest = url[_plan.StartUrl.Length..];
            return rest.Length == 0 ? "BaseUrl" : $"$\"{{BaseUrl}}{CSharp.Escape(rest, interpolated: true)}\"";
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var target) && Uri.TryCreate(_plan.StartUrl, UriKind.Absolute, out var start)
            && target.GetLeftPart(UriPartial.Authority) == start.GetLeftPart(UriPartial.Authority))
        {
            _stepUsesFlow = true;
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

    private GeneratedFlow Assemble(string namespaceName)
    {
        var className = _plan.ClassName;
        var configName = $"{className}Config";
        var code = new StringBuilder();
        var checks = new List<CheckByHand>();
        var actionLines = new Dictionary<string, LineSpan>();
        var stepLines = new Dictionary<string, LineSpan>();
        var lineNumber = 0;
        void Append(string text, string? check = null, string? action = null)
        {
            code.Append(text).Append('\n');
            lineNumber++;
            if (check is not null) checks.Add(new CheckByHand(lineNumber, check));
            if (action is not null)
                actionLines[action] = actionLines.TryGetValue(action, out var span) ? span with { Last = lineNumber }
                    : new LineSpan(lineNumber, lineNumber);
        }

        var all = _run.Concat(_methods.SelectMany(method => method.Body)).Select(line => line.Text).ToList();
        var usesUpload = all.Any(text => text.Contains("FileToUpload(", StringComparison.Ordinal));
        if (all.Any(text => text.Contains("Regex(", StringComparison.Ordinal))) Append("using System.Text.RegularExpressions;");
        Append("using Microsoft.Playwright;");
        Append("using QaaS.Playwright;");
        if (all.Any(text => text.Contains("Expect(", StringComparison.Ordinal))) Append("using static Microsoft.Playwright.Assertions;");
        Append("");
        Append($"namespace {namespaceName};");
        Append("");
        Append("/// <summary>");
        if (_plan.After is { } after)
        {
            Append($"/// Recorded on {XmlText(_plan.StartUrl)} after <c>{after}</c>, so it runs after that flow:");
            Append($"/// <c>SetupFlows: [{after}]</c>. The values it types and chooses are the settings in");
        }
        else
            Append($"/// Recorded on {XmlText(_plan.StartUrl)}. The values it types and chooses are the settings in");
        Append($"/// <see cref=\"{configName}\"/>: give each case its own under FlowConfiguration:{className}:.");
        Append("/// </summary>");
        Append($"public sealed class {className} : BasePlaywrightFlow<{configName}>");
        Append("{");
        Append("    public override async Task RunAsync(IPage page)");
        Append("    {");
        foreach (var line in _run) Append($"        {line.Text}", line.Check);
        Append("    }");
        foreach (var (step, signature, body) in _methods)
        {
            Append("");
            Append($"    {signature}");
            var first = lineNumber;
            Append("    {");
            foreach (var line in body) Append($"        {line.Text}", line.Check, line.Action);
            Append("    }");
            stepLines[step] = new LineSpan(first, lineNumber);
        }

        if (usesUpload)
        {
            Append("");
            foreach (var line in FileToUploadMethod.Split('\n')) Append(line);
        }

        Append("}");
        Append("");
        Append($"/// <summary>The values recorded in <see cref=\"{className}\"/>; FlowConfiguration:{className}: sets them per case.</summary>");
        if (_plan.Settings.Count == 0)
            Append($"public sealed record {configName};");
        else
        {
            Append($"public sealed record {configName}");
            Append("{");
            foreach (var setting in _plan.Settings)
                Append($"    public {setting.Type} {setting.Name} {{ get; init; }} = {Default(setting)};");
            Append("}");
        }

        foreach (var missing in _plan.NotRecorded) checks.Add(new CheckByHand(null, missing));
        return new GeneratedFlow(code.ToString(), FlowYaml.Write(className, _plan.Settings), checks)
        {
            ActionLines = actionLines, StepLines = stepLines,
        };
    }

    private static string Default(Setting setting) =>
        setting.Flag is { } flag ? CSharp.Bool(flag) : CSharp.Literal(setting.Text ?? "");

    // A URL in an XML doc comment.
    private static string XmlText(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    [GeneratedRegex(@"[\\^$.|?*+()\[\]{}]")]
    private static partial Regex RegexSpecial();
}
