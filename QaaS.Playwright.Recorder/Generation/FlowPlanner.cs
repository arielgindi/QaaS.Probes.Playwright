using QaaS.Playwright.Recorder.Capture;

namespace QaaS.Playwright.Recorder.Generation;

/// <summary>
/// Turns a recording into a <see cref="FlowPlan"/>: drops what is only the way to an action (opening a Select, focusing a
/// field), joins what belongs together (the text typed into an Autocomplete and the option then chosen), gives each
/// recorded value a setting, and splits the actions into steps where a dialog opens or closes or the page changes.
/// </summary>
internal static class FlowPlanner
{
    private static readonly HashSet<string> Boundaries = ["dialogOpened", "dialogClosed", "urlChanged", "popup"];
    private static readonly HashSet<string> ValueKinds = ["fill", "choose", "check", "uncheck", "upload"];

    public static FlowPlan Plan(Recording recording, string className)
    {
        var plan = new FlowPlan { ClassName = className, StartUrl = recording.StartUrl };
        plan.NotRecorded.AddRange(recording.NotRecorded ?? []);
        var actions = Clean(recording.Actions.Select(Prepare).ToList());
        PlaceInDialogs(actions);
        AssignSettings(actions, plan.Settings);
        SplitIntoSteps(actions, plan);
        StepNamer.Name(plan);
        return plan;
    }

    private static PlannedAction Prepare(CapturedAction source)
    {
        var element = source.Kind == "choose" ? source.Field ?? source.Target : source.Target;
        var planned = new PlannedAction { Source = source };
        planned.Locator = LocatorPolicy.Choose(element, byName: source.Widget == "radio");
        planned.Key = planned.Locator switch
        {
            null => $"{source.Kind}:{element?.Tag}:{element?.Text}",
            { Kind: "generator" } generated => generated.Csharp!,
            var chosen => LocatorPolicy.Render("page", chosen.Parts),
        };
        planned.Effects.AddRange(source.Effects);
        return planned;
    }

    // What a person does on the way to an action is not a step of its own.
    private static List<PlannedAction> Clean(List<PlannedAction> actions)
    {
        var kept = new List<PlannedAction>();
        for (var index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            var last = kept.LastOrDefault();
            var widget = action.Source.Widget;
            var noEffect = action.Effects.Count == 0;
            switch (action.Kind)
            {
                // Choosing an option opens its Select first.
                case "click" when widget == "select-open":
                // The arrow that opens an Autocomplete's list, when an option of it is chosen next.
                case "click" when widget == "autocomplete-button" && ChoiceFollows(actions, index, action.Source.Field):
                // A click beside every control, e.g. to leave a field, that changed nothing.
                case "click" or "dblclick" when action.Source.Target is not { Interactive: true } && noEffect:
                case "press" when action.Source.Key == "Escape" && noEffect:
                    continue;
                case "choose" when widget == "autocomplete" && last is { Kind: "fill" } && last.Key == action.Key:
                    action.Search = last.Source.Value;
                    action.Effects.InsertRange(0, last.Effects);
                    kept.RemoveAt(kept.Count - 1);
                    break;
                case "fill" when last is { Kind: "fill" } && last.Key == action.Key && last.Effects.Count == 0:
                case "check" or "uncheck" when last is { Kind: "check" or "uncheck" } && last.Key == action.Key
                                               && last.Effects.Count == 0:
                case "dblclick" when last is { Kind: "click" } && last.Key == action.Key:
                    kept.RemoveAt(kept.Count - 1);
                    break;
            }

            kept.Add(action);
        }

        return kept;
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

    // A dialog that closed and opened again within one action was only rendered anew.
    private static void DropFlicker(PlannedAction action)
    {
        var titles = action.Effects.Where(effect => effect.Kind == "dialogOpened").Select(effect => effect.Dialog?.Title)
            .Intersect(action.Effects.Where(effect => effect.Kind == "dialogClosed").Select(effect => effect.Dialog?.Title))
            .ToHashSet();
        action.Effects.RemoveAll(effect => effect.Kind is "dialogOpened" or "dialogClosed" && titles.Contains(effect.Dialog?.Title));
    }

    // Every value the person typed, chose or checked becomes a setting, named after the field's path.
    private static void AssignSettings(List<PlannedAction> actions, List<Setting> settings)
    {
        Setting Add(string name, string? text, bool? flag = null)
        {
            var unique = name;
            for (var copy = 2; settings.Any(setting => setting.Name == unique); copy++) unique = $"{name}{copy}";
            var setting = new Setting(unique, text, flag);
            settings.Add(setting);
            return setting;
        }

        foreach (var action in actions)
        {
            var source = action.Source;
            var field = source.Kind == "choose" ? source.Field ?? source.Target : source.Target;
            var name = SettingName(field);
            switch (action.Kind)
            {
                case "fill":
                    action.Value = Add(name, source.Value ?? "");
                    break;
                case "check" or "uncheck" when source.Widget == "radio":
                    action.Value = Add(name, action.Locator?.Kind == "nameValue" ? field?.Value : field?.Text);
                    break;
                case "check" or "uncheck":
                    action.Value = Add(name, null, action.Kind == "check");
                    break;
                case "choose":
                    AssignChoice(action, name, Add);
                    break;
            }

            if (action.Locator?.Kind == "row" || NameHoldsRowKey(action))
            {
                // Actions on one row read one setting.
                var key = source.Target!.Row!.Key;
                var header = CSharp.Pascal(source.Target.Row.Header) ?? "RowKey";
                action.RowKey = settings.FirstOrDefault(setting => setting.Text == key && setting.Name.StartsWith(header,
                    StringComparison.Ordinal)) ?? Add(header, key);
            }
        }
    }

    private static void AssignChoice(PlannedAction action, string name, Func<string, string?, bool?, Setting> add)
    {
        var source = action.Source;
        var option = source.Value ?? "";
        var settled = source.Effects.FirstOrDefault(effect => effect.Kind == "settled");
        if (source.Widget == "autocomplete")
        {
            var shown = settled?.Raw is { } raw ? Bidi.Strip(raw) : option;
            if (action.Search is not null) action.SearchValue = add(name + "Search", action.Search, null);
            action.Value = add(name, shown, null);
            if (shown != option) action.OptionValue = add(name + "Option", option, null);
            return;
        }

        action.Value = add(name, option, null);
        if (source.Widget == "select" && settled?.Value is { } text && text != option)
            action.ShownValue = add(name + "Shown", text, null);
    }

    public static bool NameHoldsRowKey(PlannedAction action) =>
        action.Locator is { Kind: "role" } role && action.Source.Target?.Row?.Key is { Length: >= 2 } key
        && role.Parts.LastOrDefault()?.Name?.Contains(key, StringComparison.Ordinal) == true;

    // The field's path, as the form names it: a Select's hidden input, the name, a stable id or test id; else its label.
    private static string SettingName(CapturedTarget? field)
    {
        if (field is null) return "Value";
        var select = field.Candidates.FirstOrDefault(candidate => candidate.Kind == "select")?.Value;
        var groupMember = field.Role == "checkbox" && field.Candidates.Any(candidate => candidate is { Kind: "name", Count: > 1 });
        var path = new[] { select, field.Name, field.Id, field.TestId }.FirstOrDefault(value => !UnstableId.IsUnstable(value));
        var name = CSharp.Pascal(path) ?? (CSharp.IsAsciiText(field.Label) ? CSharp.Pascal(field.Label) : null)
                   ?? (CSharp.IsAsciiText(field.Text) ? CSharp.Pascal(field.Text) : null) ?? "Value";
        if (groupMember && path == field.Name)
            name += CSharp.Pascal(field.Value) ?? CSharp.Pascal(field.Text) ?? "";
        return name;
    }

    private static void SplitIntoSteps(List<PlannedAction> actions, FlowPlan plan)
    {
        Step? current = null;
        foreach (var action in actions)
        {
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

        // What the person filled in and the click that saves it are two steps, as a person tells them; so are the
        // values and the clicks that open a dialog (a row's menu, then its item).
        for (var index = 0; index < plan.Steps.Count; index++)
        {
            var step = plan.Steps[index];
            var lastValue = step.Actions.FindLastIndex(action => ValueKinds.Contains(action.Kind));
            if (!step.Ended || lastValue < 0 || lastValue == step.Actions.Count - 1) continue;
            var opens = step.Actions[^1].Effects.Any(effect => effect.Kind is "dialogOpened" or "popup");
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
            step.OpensPage = last.Kind == "newTab" ? last.Source.Page
                : last.Effects.FirstOrDefault(effect => effect.Kind == "popup")?.Page;
        }
    }
}
