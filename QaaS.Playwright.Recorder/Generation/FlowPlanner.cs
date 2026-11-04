using System.Globalization;
using System.Text.RegularExpressions;
using QaaS.Playwright.Recorder.Capture;

namespace QaaS.Playwright.Recorder.Generation;

/// <summary>
/// Turns a recording into a <see cref="FlowPlan"/>: drops what is only the way to an action (opening a Select, focusing a
/// field), joins what belongs together (the text typed into an Autocomplete and the option then chosen), gives each
/// recorded value a setting, splits the actions into steps where a dialog opens or closes or the page changes, folds
/// what was done again and again into a loop, and ends a step where the page showed the result of an action. A check
/// the person chose goes with the action before it, and the person's edits of the settings apply last.
/// </summary>
internal static partial class FlowPlanner
{
    private static readonly HashSet<string> Boundaries = ["dialogOpened", "dialogClosed", "urlChanged", "popup"];
    private static readonly HashSet<string> ValueKinds = ["fill", "choose", "check", "uncheck", "upload"];

    // Members every record has, which a setting must not hide.
    private static readonly string[] RecordMembers =
        ["Equals", "GetHashCode", "ToString", "GetType", "EqualityContract", "Deconstruct", "PrintMembers", "Finalize"];

    // A step of the values the person filled in keeps the click that sends them when it has this many at most, as a
    // search box or a login does.
    private const int ValuesKeptWithTheirClick = 2;

    public static FlowPlan Plan(Recording recording, string className)
    {
        Repair.Recording(recording);
        var plan = new FlowPlan
        {
            ClassName = className, StartUrl = recording.StartUrl, After = recording.After, AutoChecks = recording.AutoChecks,
        };
        plan.NotRecorded.AddRange((recording.NotRecorded ?? []).Where(missing => !string.IsNullOrWhiteSpace(missing)));
        var prepared = recording.Actions.Select(Prepare).ToList();
        // A click on no control that only an id made anew on each load finds cannot replay: left out, and said.
        foreach (var lost in prepared.Where(OnlyByGeneratedId).ToList())
        {
            prepared.Remove(lost);
            plan.NotRecorded.Add($"a click on {Names.Human(lost.Source.Target) ?? $"a {lost.Source.Target?.Tag ?? "element"}"} that only an id " +
                                 "made anew on each load finds was left out: redo it by hand if the flow needs it");
        }

        var actions = AttachChecks(prepared, Clean([.. prepared.Where(action => action.Kind != "expect")]));
        PlaceInDialogs(actions);
        actions = CloseUnseen(actions);
        AssignSettings(actions);
        SplitIntoSteps(actions, plan, LoopFinder.Find(actions));
        SplitAtResults(plan);
        SplitAtSections(plan);
        ApplyEdits(plan);
        CollectSettings(plan);
        StepNamer.Name(plan);
        plan.Suggestions.AddRange(Noise.Suggest([.. plan.Steps.SelectMany(step => step.Actions)]));
        return plan;
    }

    private static PlannedAction Prepare(CapturedAction source)
    {
        var element = source.Element;
        var planned = new PlannedAction { Source = source };
        planned.Locator = LocatorPolicy.Choose(element, byName: source.Widget == "radio" || LocatorPolicy.IsNumberedToggle(element));
        planned.Key = planned.Locator switch
        {
            null => $"{source.Kind}:{element?.Tag}:{element?.Text}",
            { Kind: "generator" } generated => generated.Csharp!,
            var chosen => LocatorPolicy.Render("page", chosen.Parts),
        };
        planned.Effects.AddRange(source.Effects);
        return planned;
    }

    private static bool OnlyByGeneratedId(PlannedAction action) =>
        action is { Kind: "click" or "dblclick", Locator: { Kind: "generator" } generated } && !Noise.IsControl(action.Source.Target)
        && !Noise.HasResult(action) && LocatorPolicy.Problem(generated) == LocatorPolicy.GeneratedIdProblem;

    // What a person does on the way to an action is not a step of its own: opening a select, a click that only put the
    // focus in a field, a click on no control that changed nothing, a drag that dropped nowhere, the click that opened
    // the list of the choice next, and the text typed into an autocomplete to filter the list it then chose from.
    private static List<PlannedAction> Clean(List<PlannedAction> actions)
    {
        var kept = new List<PlannedAction>();
        for (var index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            var last = kept.LastOrDefault();
            var next = index + 1 < actions.Count ? actions[index + 1] : null;
            var widget = action.Source.Widget;
            var noEffect = action.Effects.Count == 0;
            // What a click beside every control changed, as a field it left closing its list, is no result of its own.
            var noResult = !Noise.HasResult(action);
            switch (action.Kind)
            {
                // A list opened, then closed with Escape that showed nothing: both, as the flow opens it as it chooses.
                case "press" when action.Source.Key == "Escape" && noEffect && last is { Kind: "click" } opening && OpensAList(opening):
                    kept.RemoveAt(kept.Count - 1);
                    continue;
                // Choosing an option opens its Select first.
                case "click" when widget == "select-open":
                // The arrow that opens an Autocomplete's list, when an option of it is chosen next.
                case "click" when widget == "autocomplete-button" && ChoiceFollows(actions, index, action.Source.Field):
                // A click beside every control, e.g. to leave a field, that changed nothing; or on a native select, whose
                // choice the flow makes without opening it.
                case "click" or "dblclick" when action.Source.Target is not { Interactive: true } && noResult:
                case "click" when action.Source.Target?.Tag == "select" && noEffect:
                case "click" or "dblclick" when Noise.DidNothing(action):
                case "click" when Noise.OnlyFocuses(action, next):
                case "drag" when Noise.DroppedNowhere(action):
                case "press" when action.Source.Key == "Escape" && noEffect:
                // The address the app was at when a pause or a try ended, right before a click on a menu's item or a tab
                // that goes elsewhere itself, as a menu is on every page: the flow need not go there first.
                case "navigate" when action.Source.AfterPause == true && next is { Kind: "click" } && next.Has("urlChanged")
                                     && next.Source.Target?.Role is "menuitem" or "tab":
                    continue;
                // The click that opened the list of the choice next, which the flow makes as it writes the choice: by the
                // capture script's word, or a click on the field itself.
                // Not for an option of no role, whose field may be no element to click (antd's hidden input): the click
                // that opened its list stays, and the flow chooses in the list it opened.
                case "choose" when last is { Kind: "click" } && (last.Source.Id == action.Source.Opener || last.Key == action.Key
                                                                   || OpensItsField(last, action))
                                   && !last.Effects.Any(effect => Boundaries.Contains(effect.Kind)) && action.Source.OptionCss is null:
                    kept.RemoveAt(kept.Count - 1);
                    if (kept.LastOrDefault() is { Kind: "fill" } typed && typed.Key == action.Key && widget == "autocomplete")
                        TakeSearch(action, typed, kept);
                    break;
                // A select whose list was opened by the click before the text typed into the list's own filter (Kendo's): the
                // flow keeps both, and chooses in the list they left open.
                case "choose" when widget == "select" && last is { Kind: "fill" } && kept.Count >= 2 && kept[^2] is { Kind: "click" } opener
                                   && opener.Key == action.Key && !Noise.HasResult(opener):
                    action.Opened = true;
                    break;
                // What was typed to filter the list is part of the choice, never a value of its own.
                case "choose" when widget == "autocomplete" && last is { Kind: "fill" } && last.Key == action.Key:
                    TakeSearch(action, last, kept);
                    break;
                // Typing on in a field: the value it ends with is the one typed.
                case "fill" when last is { Kind: "fill" } && last.Key == action.Key && last.Effects.Count == 0
                                 && (action.Source.Value ?? "").StartsWith(last.Source.Value ?? "", StringComparison.Ordinal):
                // Ticked again, or turned on and off: the state it ends in, which a suggestion leaves out where it is the
                // one it had before; a radio of the group picked again, the last.
                case "check" or "uncheck" when last is { Kind: "check" or "uncheck" } && last.Key == action.Key && !Noise.HasResult(last):
                    action.Before = last.Before ?? last.Kind != "check";
                    kept.RemoveAt(kept.Count - 1);
                    break;
                case "check" when widget == "radio" && last is { Kind: "check", Source.Widget: "radio" } && !Noise.HasResult(last)
                                  && last.Source.Target?.Name is { Length: > 0 } group && action.Source.Target?.Name == group:
                case "dblclick" when last is { Kind: "click" } && last.Key == action.Key:
                    kept.RemoveAt(kept.Count - 1);
                    break;
            }

            kept.Add(action);
        }

        return kept;
    }

    // A click on a select or a combobox that showed nothing but what it opened: its list.
    private static bool OpensAList(PlannedAction click) =>
        !Noise.HasResult(click) && (click.Source.Widget is "select-open" or "autocomplete-button"
                                    || click.Source.Target is { Role: "combobox" or "listbox" } or { Tag: "select" });

    // A click on what is no control just before a choice of a field, which only opened its list: the widget's arrow, or
    // what holds it. The flow opens the list by the field as it writes the choice. A list with no field of its own is
    // opened by that click, which stays.
    private static bool OpensItsField(PlannedAction click, PlannedAction choice) =>
        choice.Source is { Widget: "select" or "autocomplete", Field: not null } && click.Effects.Count == 0
        && !Noise.IsControl(click.Source.Target);

    private static void TakeSearch(PlannedAction choice, PlannedAction typed, List<PlannedAction> kept)
    {
        choice.Search = typed.Source.Value;
        choice.Effects.InsertRange(0, typed.Effects);
        kept.Remove(typed);
    }

    // A check goes with the action before it that the flow does; one before every action is a step's action itself, and
    // so is one of another page than that action's, as after going back to a tab, which the step of the action has not.
    // By where each is in the recording, as two actions of one id, an action copied, are two places still.
    private static List<PlannedAction> AttachChecks(List<PlannedAction> recorded, List<PlannedAction> kept)
    {
        var at = recorded.Select((action, index) => (action, index)).ToDictionary(pair => pair.action, pair => pair.index);
        var leading = new List<PlannedAction>();
        var after = new Dictionary<PlannedAction, List<PlannedAction>>();
        foreach (var check in recorded.Where(action => action.Kind == "expect"))
        {
            if (kept.LastOrDefault(action => at[action] < at[check]) is not { } before) leading.Add(check);
            else if (before.Source.Page == check.Source.Page) before.Checks.Add(check.Source);
            else (after.TryGetValue(before, out var own) ? own : after[before] = []).Add(check);
        }

        return [.. leading, .. kept.SelectMany(action => after.TryGetValue(action, out var own) ? [action, .. own] : new[] { action })];
    }

    private static bool ChoiceFollows(List<PlannedAction> actions, int index, CapturedTarget? field)
    {
        var key = LocatorPolicy.Choose(field) is { Kind: not "generator" } chosen
            ? LocatorPolicy.Render("page", chosen.Parts)
            : null;
        foreach (var next in actions.Skip(index + 1))
        {
            if (next.Kind == "fill" && next.Key == key) continue;
            return next.Kind == "choose" && next.Key == key;
        }

        return false;
    }

    // Each action belongs to the dialog its element is in. An option or menu item shows outside every dialog, but is
    // part of what is done in the dialog still open behind it. A dialog that closed or opened unseen between two
    // actions (a slow save) is told by which dialogs were open at the next one.
    private static void PlaceInDialogs(List<PlannedAction> actions)
    {
        for (var index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            var previous = index > 0 ? actions[index - 1] : null;
            action.Dialog = action.Source.Dialog?.Title is { Length: > 0 } title ? title
                : previous?.Dialog is { } open && action.Source.OpenDialogs?.Contains(open) == true
                  && !previous.Has("dialogClosed") ? open
                : null;
            DropFlicker(action);
        }

        for (var index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            var next = index + 1 < actions.Count ? actions[index + 1] : null;
            if (action.Dialog is { } dialog && next is { Source.OpenDialogs: { } openAtNext }
                && !openAtNext.Contains(dialog) && !ClosesOwnDialog(action))
                action.Effects.Add(new CapturedEffect { Kind = "dialogClosed", Dialog = action.Source.Dialog });

            if (next?.Dialog is { } nextDialog && nextDialog != action.Dialog
                && action.Source.OpenDialogs?.Contains(nextDialog) == false
                && !action.Effects.Any(effect => effect.Kind == "dialogOpened" && effect.Dialog?.Title == nextDialog))
                action.Effects.Add(new CapturedEffect { Kind = "dialogOpened", Dialog = next.Source.Dialog });
        }
    }

    private static bool ClosesOwnDialog(PlannedAction action) =>
        action.Effects.Any(effect => effect.Kind == "dialogClosed" && effect.Dialog?.Title == action.Dialog);

    // A dialog that closed and opened again within one action was only rendered anew. One that opened, and closed a
    // while later with no action seen in between, was closed by what the recorder did not see: a key, a click outside it.
    private static void DropFlicker(PlannedAction action)
    {
        var titles = action.Effects.Where(effect => effect.Kind == "dialogOpened").Select(effect => effect.Dialog?.Title)
            .Intersect(action.Effects.Where(effect => effect.Kind == "dialogClosed").Select(effect => effect.Dialog?.Title))
            .ToHashSet();
        foreach (var title in titles.ToList())
        {
            var opened = action.Effects.First(effect => effect.Kind == "dialogOpened" && effect.Dialog?.Title == title);
            var closed = action.Effects.Last(effect => effect.Kind == "dialogClosed" && effect.Dialog?.Title == title);
            if (closed.T - opened.T < UnseenCloseMs) continue;
            action.Effects.RemoveAll(effect => effect.Kind == "dialogClosed" && effect.Dialog?.Title == title);
            action.Unseen.Add(closed);
            titles.Remove(title);
        }

        action.Effects.RemoveAll(effect => effect.Kind is "dialogOpened" or "dialogClosed" && titles.Contains(effect.Dialog?.Title));
    }

    // Longer than a render takes between a dialog's opening and its closing: someone closed it.
    private const long UnseenCloseMs = 300;

    // After an action whose dialog closed unseen, the flow closes it with Escape, which a person may need to change.
    private static List<PlannedAction> CloseUnseen(List<PlannedAction> actions)
    {
        var all = new List<PlannedAction>();
        foreach (var action in actions)
        {
            all.Add(action);
            foreach (var closed in action.Unseen)
            {
                var escape = new CapturedAction
                {
                    Id = $"{action.Source.Id}-closed", T = closed.T, Kind = "press", Key = "Escape", OnPage = true, Page = action.Source.Page,
                    Url = action.Source.Url, Dialog = closed.Dialog, OpenDialogs = closed.Dialog?.Title is { } title ? [title] : [], Guessed = true,
                };
                var planned = new PlannedAction { Source = escape, Key = $"press:Escape:{closed.Dialog?.Title}", Dialog = closed.Dialog?.Title };
                planned.Effects.Add(closed);
                all.Add(planned);
            }
        }

        return all;
    }

    // Every value the person typed, chose or checked becomes a setting, named after the field's path; the names are
    // told apart once the flow's settings are known (CollectSettings).
    private static void AssignSettings(List<PlannedAction> actions)
    {
        var created = new List<Setting>();
        foreach (var action in actions)
        {
            var source = action.Source;
            var field = source.Kind == "choose" ? source.Field ?? source.Target : source.Target;
            var name = SettingName(field);
            var subject = action.Dialog is { } dialog && CSharp.IsAsciiText(dialog) && !dialog.Any(char.IsDigit)
                ? CSharp.Pascal(dialog)
                : null;
            var qualified = subject is null ? null : subject + name;
            // A choice whose field says nothing of what it is for is named after its dialog: what the Shipping dialog chooses.
            if (source.Kind == "choose" && name == "Value" && subject is not null) (name, qualified) = (subject, null);
            switch (action.Kind)
            {
                case "fill":
                {
                    // Text typed into an autocomplete and left there, no choice of it, is its text: the choices are its name's.
                    if (source.Widget == "autocomplete") (name, qualified) = (name + "Text", qualified is null ? null : qualified + "Text");
                    // The same words typed again, into another field, are the same value: a user's name searched for after
                    // it was given to the user reads the same setting, so a case that changes one changes both. Never a
                    // password, nor what a secret's field holds: a demo's password may be its user name too ("demo"),
                    // and the flow would then type the user name as the password, with no setting for the password.
                    var secret = IsSecretField(field, name);
                    if (!secret && source.Value is { Length: >= 4 } again && again.Any(char.IsLetter) && created.LastOrDefault(setting =>
                            setting is { Type: "string", Secret: false, Flag: null } && setting.Text == again && setting.Name != "Value"
                            && !Configuration.Secrets.IsSecretKey(setting.Name)) is { } earlier
                        && actions.Any(other => other.Kind == "fill" && other.Value == earlier
                                                && (other.Key != action.Key || Path(other.Source.Url) != Path(action.Source.Url))))
                    {
                        action.Value = earlier;
                        break;
                    }

                    action.Value = Typed(name, source.Value ?? "", field, qualified);
                    // A secret is text, whatever its field takes, as its YAML reads it from the environment.
                    if (secret) (action.Value.Secret, action.Value.Type) = (true, "string");
                    // A date or a time input takes its value in one format, whatever the browser shows.
                    action.Value.Format = field?.Type switch
                    {
                        "date" => "yyyy-MM-dd", "datetime-local" => "yyyy-MM-ddTHH:mm", "time" => "HH:mm", "month" => "yyyy-MM", "week" => "yyyy-Www",
                        _ => null,
                    };
                    break;
                }
                // One file uploaded is a setting of its path, named after its field's label, its default the name the
                // browser told; several stay the flow's FileToUpload to fill in.
                case "upload" when source.Files is [var file]:
                    action.Value = new Setting(Names.Upload(field), file) { Qualified = qualified };
                    break;
                case "check" or "uncheck" when source.Widget == "radio":
                    action.Value = new Setting(name, action.Locator?.Kind == "nameValue" ? field?.Value : field?.Text)
                        { Qualified = qualified };
                    break;
                case "check" or "uncheck":
                    action.Value = new Setting(name, null, action.Kind == "check") { Qualified = qualified };
                    if (Names.Group(field) is { } group) action.Member = new Setting(group, field!.Text);
                    break;
                case "choose":
                    AssignChoice(action, ChoiceName(name), qualified is null ? null : ChoiceName(qualified));
                    break;
            }

            created.AddRange(action.Values);
            // A row's locator of an element that says nothing of its row, as a field chosen in has none, reads no setting.
            if ((action.Locator?.Kind == "row" || NameHoldsRowKey(action)) && source.Target?.Row is { Key.Length: > 0 } row)
            {
                // Actions on one row read one setting, as does a value typed before that the row shows.
                var key = row.Key;
                var header = CSharp.Pascal(row.Header) ?? "RowKey";
                var verb = CSharp.Pascal(StepNamer.Label(action, without: key));
                action.RowKey = created.FirstOrDefault(setting => setting.Text == key
                                                                  && setting.Name.StartsWith(header, StringComparison.Ordinal));
                if (action.RowKey is null)
                {
                    action.RowKey = new Setting(header, key) { Qualified = verb is null ? null : $"{header}To{verb}" };
                    created.Add(action.RowKey);
                }
            }
        }
    }

    // A password field, or one whose setting's name says it holds a secret (a token, an API key).
    private static bool IsSecretField(CapturedTarget? field, string name) =>
        field?.Type == "password" || Configuration.Secrets.IsSecretKey(name);

    // A number field's value as a number, when it is one that reads back the same.
    private static Setting Typed(string name, string text, CapturedTarget? field, string? qualified)
    {
        var type = field?.Type != "number" ? "string"
            : WholeNumber().IsMatch(text) && int.TryParse(text, CultureInfo.InvariantCulture, out _) ? "int"
            : DecimalNumber().IsMatch(text) ? "decimal"
            : "string";
        return new Setting(name, text) { Type = type, Qualified = qualified };
    }

    // A choice holds the option's text, not the id a form keeps for it: maintenanceTypeId gives MaintenanceType.
    private static string ChoiceName(string name) =>
        name.Length > 2 && name.EndsWith("Id", StringComparison.Ordinal) && char.IsLower(name[^3]) ? name[..^2] : name;

    private static void AssignChoice(PlannedAction action, string name, string? qualified)
    {
        var source = action.Source;
        var option = source.Value ?? "";
        var settled = source.Effects.FirstOrDefault(effect => effect.Kind == "settled");
        Setting Add(string suffix, string text) =>
            new(name + suffix, text) { Qualified = qualified is null ? null : qualified + suffix };
        if (source.Widget == "autocomplete")
        {
            var shown = settled?.Raw is { Length: > 0 } raw ? Bidi.Strip(raw) : option;
            // What the person typed to find the option is the search, a setting of its own: the flow types it, as a site
            // may find nothing by the option's whole text (react-admin's customers by their first name only), then chooses
            // the option. A search the option's text is all of needs none.
            if (action.Search is { Length: > 0 } search && search != option && search != shown)
                action.SearchValue = Add("Search", search);
            action.Value = Add("", shown);
            if (shown != option) action.OptionValue = Add("Option", option);
            return;
        }

        action.Value = Add("", option);
        // What a select shows after the choice, when it shows more than the option's words; never of a multi-select,
        // whose other choices it shows too, the record's own (Regular as well as Returns).
        if (source.Widget == "select" && settled is { Value: { Length: > 0 } text, Multi: not true } && text != option && source.Field?.Tag != "input")
            action.ShownValue = Add("Shown", text);
    }

    public static bool NameHoldsRowKey(PlannedAction action) =>
        action.Locator is { Kind: "role" } role && action.Source.Target?.Row?.Key is { Length: >= 2 } key
        && role.Parts.LastOrDefault()?.Name?.Contains(key, StringComparison.Ordinal) == true;

    /// <summary>
    /// Each action of the flow, by its id, with what only led to it since the action before it (opening a Select, typing
    /// into an Autocomplete, a click that only put the focus in its field) and the checks after it: what leaving it out
    /// leaves out.
    /// </summary>
    public static Dictionary<string, IReadOnlyList<string>> Sources(Recording recording, IReadOnlyList<PlannedAction> planned)
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

    private static string? Path(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;

    /// <summary>The setting of a field's value, as <see cref="Names.Setting"/> names it.</summary>
    internal static string SettingName(CapturedTarget? field) => Names.Setting(field);

    private static void SplitIntoSteps(List<PlannedAction> actions, FlowPlan plan, List<(int Start, int Length, Loop Loop)> loops)
    {
        Step? current = null;
        for (var index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            if (loops.FirstOrDefault(found => found.Start == index) is { Loop: { } loop })
            {
                if (current is not null) plan.Steps.Add(current);
                current = null;
                var looped = new Step { Page = action.Source.Page, Dialog = action.Dialog, Loop = loop };
                looped.Actions.AddRange(actions.GetRange(index, loop.Length * loop.Repeats));
                plan.Steps.Add(looped);
                index += loop.Length * loop.Repeats - 1;
                continue;
            }

            var page = action.Kind == "newTab" ? current?.Page ?? "page" : action.Source.Page;
            var dialog = action.Kind == "newTab" ? current?.Dialog : action.Dialog;
            if (current is not null && (current.Ended || current.Page != page || current.Dialog != dialog))
            {
                plan.Steps.Add(current);
                current = null;
            }

            current ??= new Step { Page = page, Dialog = dialog };
            current.Actions.Add(action);
            if (action.Kind == "newTab" || action.Effects.Any(effect => Boundaries.Contains(effect.Kind)))
                current.Ended = true;
        }

        if (current is not null) plan.Steps.Add(current);
    }

    // A click the page answered with a message or a wait ends a step too: it did what the person set out to do. What
    // the person filled in and the click that sends it are then two steps, as a person tells them, unless they are one
    // or two values, as a search or a login; so are the values and the clicks that open a dialog (a row's menu, then
    // its item).
    private static void SplitAtResults(FlowPlan plan)
    {
        for (var index = 0; index < plan.Steps.Count; index++)
        {
            var step = plan.Steps[index];
            if (step.Loop is not null) continue;
            var result = step.Actions.FindIndex(action => action.Kind is "click" or "dblclick" or "press"
                                                          && action.Effects.Any(effect => effect.Kind is "alert" or "busy"));
            if (result >= 0 && result < step.Actions.Count - 1)
            {
                var after = new Step { Page = step.Page, Dialog = step.Dialog, Ended = step.Ended };
                after.Actions.AddRange(step.Actions.Skip(result + 1));
                step.Actions.RemoveRange(result + 1, step.Actions.Count - result - 1);
                plan.Steps.Insert(index + 1, after);
            }

            step.Ended |= result >= 0;
            var lastValue = step.Actions.FindLastIndex(action => ValueKinds.Contains(action.Kind));
            if (!step.Ended || lastValue < 0 || lastValue == step.Actions.Count - 1) continue;
            var opens = step.Actions[^1].Effects.Any(effect => effect.Kind is "dialogOpened" or "popup");
            if (!opens && lastValue == step.Actions.Count - 2 && (Noise.Sends(step.Actions[^1]) || step.Actions[^1].Kind == "press")
                && step.Actions.Count(action => ValueKinds.Contains(action.Kind)) <= ValuesKeptWithTheirClick)
                continue;
            var from = opens ? lastValue + 1 : step.Actions.Count - 1;
            var rest = new Step { Page = step.Page, Dialog = step.Dialog, Ended = true };
            rest.Actions.AddRange(step.Actions.Skip(from));
            step.Actions.RemoveRange(from, step.Actions.Count - from);
            step.Ended = false;
            plan.Steps.Insert(index + 1, rest);
        }

        foreach (var step in plan.Steps)
        {
            var last = step.Actions[^1];
            step.Opens = last.Effects.LastOrDefault(effect => effect.Kind == "dialogOpened")?.Dialog;
            // A step returns one thing: the dialog, of an action that opened a dialog and a pop-up too.
            step.OpensPage = step.Opens is not null ? null
                : last.Kind == "newTab" ? last.Source.Page
                : last.Effects.FirstOrDefault(effect => effect.Kind == "popup")?.Page;
        }
    }

    // A step works in one section of the page, as a person goes from a form's "Personal Details" to its "Custom Fields":
    // where the heading of what the actions are in changes, a step ends.
    private static void SplitAtSections(FlowPlan plan)
    {
        for (var index = 0; index < plan.Steps.Count; index++)
        {
            var step = plan.Steps[index];
            if (step.Loop is not null || step.Opens is not null || step.OpensPage is not null) continue;
            var at = Enumerable.Range(1, Math.Max(0, step.Actions.Count - 1)).FirstOrDefault(position =>
                SectionOf(step.Actions[position - 1]) is { } before && SectionOf(step.Actions[position]) is { } after && before != after);
            if (at == 0) continue;
            var rest = new Step { Page = step.Page, Dialog = step.Dialog, Ended = step.Ended };
            rest.Actions.AddRange(step.Actions.Skip(at));
            step.Actions.RemoveRange(at, step.Actions.Count - at);
            step.Ended = false;
            plan.Steps.Insert(index + 1, rest);
        }
    }

    private static string? SectionOf(PlannedAction action) => action.Kind == "expect" ? null : action.Source.Element?.Section;

    // What the person changed about a setting in the panel: its name, its default, a run stamp, or a date from today. A
    // setting of a loop takes only a default, as its name and the way it is typed are the list's.
    private static void ApplyEdits(FlowPlan plan)
    {
        foreach (var step in plan.Steps)
            foreach (var action in step.Actions)
                foreach (var (role, setting) in action.Parts)
                {
                    if (action.Source.Edit?.Values?.GetValueOrDefault(role) is not { } edit) continue;
                    // Never a secret, which stays text, as its YAML reads it from the environment.
                    if (edit is { DateFormat: { } format, Value: { } days } && WholeNumber().IsMatch(days) && step.Loop is null
                        && setting is { Type: "string", Secret: false })
                    {
                        (setting.DateFormat, setting.Type, setting.Name) = (format, "int", RelativeDate.Name(setting.Name));
                        setting.Qualified = setting.Qualified is { } qualified ? RelativeDate.Name(qualified) : null;
                    }

                    if (edit.Value is { } value && Fits(value, setting.Type)) setting.Default = value;
                    if (step.Loop is not null) continue;
                    setting.Unique |= edit.Unique == true && setting is { Type: "string", Flag: null };
                    if (CSharp.Pascal(edit.Name) is { } name) (setting.Name, setting.Named) = (name, true);
                }
    }

    // A default the setting's type can hold, as the record and the YAML write it.
    private static bool Fits(string value, string type) => type switch
    {
        "int" => WholeNumber().IsMatch(value),
        "decimal" => WholeNumber().IsMatch(value) || DecimalNumber().IsMatch(value),
        "string" => true,
        _ => false,
    };

    /// <summary>Each setting with the step that reads it first, in the order of the steps.</summary>
    public static IEnumerable<(Step Step, Setting Setting)> SettingsByStep(FlowPlan plan)
    {
        var seen = new HashSet<Setting>();
        foreach (var step in plan.Steps)
        {
            var repeat = step.Actions.Take(step.Loop?.Length ?? 0).ToList();
            var read = step.Loop is { } loop
                ? new[] { loop.List, loop.Count }.Concat(repeat
                    .Where(action => !LoopFinder.RowFromValue(action, repeat) && action.RowKey is not null && !loop.Holds.Contains(action.RowKey))
                    .Select(action => action.RowKey))
                : step.Actions.SelectMany(action => action.Values.Append(action.RowKey));
            foreach (var setting in read.OfType<Setting>())
                if (seen.Add(setting)) yield return (step, setting);
        }
    }

    // The settings in the order of the steps, each with a name of its own: settings of one name are told apart by what
    // they are for (TicketToAssign, TicketToClose) when that tells them all apart, else by a number.
    private static void CollectSettings(FlowPlan plan)
    {
        plan.Settings.AddRange(SettingsByStep(plan).Select(read => read.Setting));
        var taken = new HashSet<string>(RecordMembers.Append($"{plan.ClassName}Config"), StringComparer.OrdinalIgnoreCase);
        // A list of texts that holds an empty one, recorded or as the person changed it, is a list of records: QaaS reads
        // an empty item of a list as none, so the list would lose it and the loop a round; an empty field it keeps.
        foreach (var list in plan.Settings.Where(setting => setting is { ItemType: null, Items: { } items, Fields: [{ Type: "string" } field] }
                                                            && items.Any(item => item[0].Value == "")))
        {
            var item = LoopFinder.Singular(list.Name);
            list.ItemType = item.Equals(list.Fields![0].Name, StringComparison.OrdinalIgnoreCase) ? item + "Item" : item;
        }

        foreach (var list in plan.Settings.Where(setting => setting.ItemType is not null))
            list.ItemType = Unique(list.ItemType!, taken);
        var named = plan.Settings.GroupBy(setting => setting.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var group in named.Where(group => group.Count() > 1 && !group.Any(setting => setting.Named)))
        {
            var qualified = group.Select(setting => setting.Qualified).ToList();
            var tellApart = qualified.All(name => name is not null)
                            && qualified.Distinct(StringComparer.OrdinalIgnoreCase).Count() == qualified.Count;
            if (tellApart)
                foreach (var setting in group) setting.Name = setting.Qualified!;
        }

        // A name the person gave keeps it.
        foreach (var setting in plan.Settings.OrderByDescending(setting => setting.Named)) setting.Name = Unique(setting.Name, taken);

        // What a setting's name says is a secret is one, as a probe hides it; text only, as a flag or a number is none.
        foreach (var setting in plan.Settings.Where(setting => setting is { Type: "string", Items: null }))
            setting.Secret |= Configuration.Secrets.IsSecretKey(setting.Name);
    }

    private static string Unique(string name, HashSet<string> taken)
    {
        var unique = name;
        for (var copy = 2; !taken.Add(unique); copy++) unique = $"{name}{copy}";
        return unique;
    }

    [GeneratedRegex(@"^-?(0|[1-9][0-9]{0,8})$")]
    private static partial Regex WholeNumber();

    [GeneratedRegex(@"^-?(0|[1-9][0-9]{0,14})\.[0-9]{1,10}$")]
    private static partial Regex DecimalNumber();
}
