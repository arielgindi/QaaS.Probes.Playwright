using System.Text.RegularExpressions;
using QaaS.Playwright.Recorder.Capture;

namespace QaaS.Playwright.Recorder.Generation;

/// <summary>
/// A change the recorder suggests to the flow, which the person applies with one click in the panel: actions to leave
/// out, as a person clicking around left them: a value typed or chosen again before anything used it, a click repeated
/// that did nothing, a choice removed again, clicks on what is no control that did nothing. Nothing is left out without
/// the person's say: what looks like noise may be what the app needs.
/// </summary>
/// <param name="Key">What it is about, the same from one plan to the next: <c>replaced:&lt;id&gt;</c>.</param>
/// <param name="Text">What the recorder saw, and what applying it does.</param>
/// <param name="Apply">The button's words: "Leave it out", "Leave these 3 out".</param>
/// <param name="Drop">The actions applying it leaves out of the flow, by their ids.</param>
/// <param name="On">The actions whose cards show it.</param>
internal sealed record Suggestion(string Key, string Text, string Apply, IReadOnlyList<string> Drop, IReadOnlyList<string> On);

/// <summary>
/// Tells what a person did on the way, which the flow does not need: a click that only put the focus in a field, a click
/// on what is no control that changed nothing, a drag that dropped nowhere; and what to suggest leaving out.
/// </summary>
internal static partial class Noise
{
    // What shows that an action did something a later one may depend on.
    private static readonly HashSet<string> Results =
        ["dialogOpened", "dialogClosed", "urlChanged", "popup", "alert", "busy", "tabSelected", "jsDialog"];

    private static readonly HashSet<string> TextRoles = ["textbox", "searchbox", "combobox", "spinbutton"];

    private static readonly HashSet<string> TextTypes = ["text", "search", "email", "tel", "url", "number", "password"];

    // The roles of what a person acts on: a click on one is kept, even when nothing showed for it.
    private static readonly HashSet<string> ControlRoles =
    [
        "button", "link", "checkbox", "radio", "switch", "tab", "menuitem", "menuitemcheckbox", "menuitemradio", "option",
        "combobox", "textbox", "searchbox", "spinbutton", "slider", "treeitem", "gridcell", "row",
    ];

    /// <summary>The action showed for itself: a dialog, an address, a message, a wait, a tab.</summary>
    public static bool HasResult(PlannedAction action) => action.Effects.Any(effect => Results.Contains(effect.Kind));

    /// <summary>
    /// A click that only put the focus in a text field: on the field, or on what holds it, which the action in the field
    /// after it does itself. Not one that showed something, nor one that opened a list of options chosen next without a
    /// field of their own, which the flow must open by that click.
    /// </summary>
    public static bool OnlyFocuses(PlannedAction click, PlannedAction? next) =>
        click.Kind == "click" && !HasResult(click)
        && (click.Source.Focus == true || IsTextField(click.Source.Target))
        && next is not { Kind: "choose", Source.Widget: "option" }
        // A list it opened, whose item the next click picks.
        && !(next is { Kind: "click" or "dblclick" } && next.Key != click.Key
             && (next.Source.Target is { Item: true } || next.Source.Target?.Role is "option" or "menuitem" or "listitem"));

    /// <summary>A text field a person types in, as the capture script described it.</summary>
    public static bool IsTextField(CapturedTarget? target) =>
        target is not null && target.Editable != false
        && (target.Editable == true || target.Tag == "textarea"
            || (target.Tag == "input" && TextTypes.Contains(target.Type ?? "text") && TextRoles.Contains(target.Role ?? "textbox")));

    /// <summary>
    /// A click on what is no control nor an item of a list (a card, a slide, an option chosen with no change to see), which
    /// the page was watched after and which changed nothing at all. A recording of an older recorder did not watch: such
    /// a click is suggested to leave out instead.
    /// </summary>
    public static bool DidNothing(PlannedAction click) =>
        click.Kind is "click" or "dblclick" && click.Source.Watched == true && click.Effects.Count == 0
        && !IsControl(click.Source.Target) && click.Source.Target?.Item != true;

    /// <summary>What a person acts on: a native control, or an element of a control's role.</summary>
    public static bool IsControl(CapturedTarget? target) =>
        target is not null && (target.Tag is "button" or "a" or "input" or "select" or "textarea" or "summary"
                               || ControlRoles.Contains(target.Role ?? ""));

    /// <summary>A drag that dropped on nothing, or on what it dragged, and did nothing: the pointer moved as it clicked.</summary>
    public static bool DroppedNowhere(PlannedAction drag) =>
        drag.Kind == "drag" && !HasResult(drag)
        && (drag.Effects.Count == 0 || drag.Source.To is not null)
        && (drag.Source.To is null || LocatorPolicy.Choose(drag.Source.To) is { } to
            && (to.Kind == "generator" ? to.Csharp : LocatorPolicy.Render("page", to.Parts)) == drag.Key);

    /// <summary>
    /// Whether a value typed at <paramref name="from"/> could have been used by the actions before <paramref name="to"/>:
    /// one that showed something, or changed the page (other than in the same field), or a check the person chose.
    /// </summary>
    private static bool UsedBetween(List<PlannedAction> actions, int from, int to, string key) =>
        actions.Skip(from + 1).Take(to - from - 1).Any(between =>
            between.Checks.Count > 0 || HasResult(between) || between.Key != key && Sends(between));

    // What sends a form's values: a submit button, or a button that says it saves, adds or sends them.
    private static readonly HashSet<string> Sending = new(StringComparer.OrdinalIgnoreCase)
    {
        "Add", "Save", "Submit", "Create", "Search", "Apply", "Send", "Update", "Confirm", "Ok", "Next", "Continue", "Login",
        "Log", "Sign", "Register", "Book", "Pay", "Upload", "Import", "Filter", "Go", "Done", "Finish", "Insert", "Enter",
    };

    private static bool Sends(PlannedAction action) =>
        action is { Kind: "click" or "dblclick" or "press" }
        && (action.Source.Target is { Type: "submit" } || action.Kind == "click" && action.Source.Target?.Role is "button" or "link"
            && StepNamer.Label(action) is { } label && Sending.Contains(StepNamer.Words(CSharp.Pascal(label) ?? "").FirstOrDefault() ?? ""));

    /// <summary>The suggestions for the actions the flow does, in their order.</summary>
    public static List<Suggestion> Suggest(List<PlannedAction> actions)
    {
        var suggestions = new List<Suggestion>();
        var suggested = new HashSet<string>();
        for (var index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            if (Replaced(actions, index) is { } replaced && suggested.Add(action.Source.Id))
                suggestions.Add(replaced);
            if (Repeated(actions, index) is { } repeated && repeated.Drop.All(suggested.Add))
                suggestions.Add(repeated);
            if (Removed(actions, index) is { } removed && removed.Drop.All(suggested.Add))
                suggestions.Add(removed);
            if (RoundTrip(actions, index) is { } roundTrip && roundTrip.Drop.All(suggested.Add))
                suggestions.Add(roundTrip);
        }

        // Clicks on what is no control that changed nothing, as far as an older recorder could tell: all at once.
        // Not one that opens a list chosen from next, which has no field to open it by, nor one on an item of a list.
        var idle = actions.Where((action, index) => action.Kind is "click" or "dblclick" && action.Source.Watched != true
                                                    && action.Effects.Count == 0 && action.Checks.Count == 0
                                                    && !IsControl(action.Source.Target) && action.Source.Target?.Role is not ("listitem" or "option")
                                                    && !suggested.Contains(action.Source.Id)
                                                    && actions.ElementAtOrDefault(index + 1) is not { Kind: "choose", Source.Widget: "option" })
            .Select(action => action.Source.Id).ToList();
        if (idle.Count > 0)
            suggestions.Add(new Suggestion($"idle:{idle[0]}",
                idle.Count == 1
                    ? "A click on what is no control, after which nothing showed: the flow likely needs it not."
                    : $"{idle.Count} clicks on what is no control, after which nothing showed: the flow likely needs none of them.",
                idle.Count == 1 ? "Leave it out" : $"Leave these {idle.Count} out", idle, idle));
        return suggestions;
    }

    // A value typed or chosen, which the same field got again later before anything used it: the flow needs the last.
    private static Suggestion? Replaced(List<PlannedAction> actions, int index)
    {
        var action = actions[index];
        var typed = action.Kind == "fill";
        var chosen = action is { Kind: "choose" } && !IsMulti(action, actions);
        if (!typed && !chosen || HasResult(action) || action.Checks.Count > 0) return null;
        var later = actions.FindIndex(index + 1, other => other.Key == action.Key && other.Kind is "fill" or "choose");
        if (later < 0 || UsedBetween(actions, index, later, action.Key)) return null;
        var again = actions[later];
        // A choice typed into to filter its list replaces the text typed before, which never was a choice.
        if (typed && again.Kind == "choose" && !IsSearchField(action, again)) return null;
        if (chosen && again.Kind != "choose") return null;
        var drop = new List<string> { action.Source.Id };
        // Enter pressed in the field right after the value, which did nothing, goes with it.
        if (typed && index + 1 < later && actions[index + 1] is { Kind: "press", Effects.Count: 0 } press && press.Key == action.Key)
            drop.Add(press.Source.Id);
        var field = Field(action);
        var value = Quote(again.Kind == "choose" ? again.Source.Value : again.Source.Value);
        var text = typed
            ? $"{field} is filled again later ({value}) before anything used “{action.Source.Value}”: the flow needs only the last."
            : $"{field} is chosen again later ({value}) before anything used “{action.Source.Value}”: the flow needs only the last.";
        return new Suggestion($"replaced:{action.Source.Id}", text, "Leave it out", drop, drop);
    }

    // The same click again right after it, which showed nothing: once is what the flow needs, or what changed nothing.
    private static Suggestion? Repeated(List<PlannedAction> actions, int index)
    {
        var action = actions[index];
        if (action.Kind != "click" || index > 0 && actions[index - 1] is { Kind: "click" } before && before.Key == action.Key
                                    && before.Dialog == action.Dialog)
            return null;
        // A column's header sorts it one way, then the other: each click counts.
        if (action.Source.Target?.Role is "columnheader" || StepNamer.Label(action)?.StartsWith("Sort", StringComparison.OrdinalIgnoreCase) == true)
            return null;
        var repeats = new List<string>();
        for (var next = index + 1; next < actions.Count; next++)
        {
            var other = actions[next];
            if (other.Kind != "click" || other.Key != action.Key || other.Dialog != action.Dialog || other.Effects.Count > 0
                || other.Checks.Count > 0)
                break;
            repeats.Add(other.Source.Id);
        }

        if (repeats.Count == 0) return null;
        var name = Quote(Names.Human(action.Source.Target)) ?? "The element";
        var times = repeats.Count + 1;
        var text = action.Effects.Count == 0
            ? $"{name} is clicked {times} times in a row, and nothing showed after any: the flow needs one click, as the next would find nothing to click once one worked."
            : $"{name} is clicked again {(repeats.Count == 1 ? "right after" : $"{repeats.Count} times after")}, and nothing showed: the flow needs the first only.";
        return new Suggestion($"repeated:{action.Source.Id}", text, repeats.Count == 1 ? "Leave the repeat out" : $"Leave the {repeats.Count} repeats out",
            repeats, [action.Source.Id, .. repeats]);
    }

    // What was done and undone before anything used it, so the page is as it was: a checkbox ticked and unticked, a
    // toggle clicked twice ("Toggle light/dark mode", a menu's group opened and closed), a choice of a menu made and made
    // back again (English, Français, English). Never what may have gone on twice, as "Add" twice adds two.
    private static Suggestion? RoundTrip(List<PlannedAction> actions, int index)
    {
        var action = actions[index];
        if (action is { Kind: "check" or "uncheck", Before: { } before } && before == (action.Kind == "check") && action.Checks.Count == 0)
            return new Suggestion($"roundTrip:{action.Source.Id}",
                $"{Quote(Names.Human(action.Source.Target)) ?? "The checkbox"} is " +
                (action.Source is { Widget: "switch", Target.Role: not "checkbox" } ? before ? "turned off and on" : "turned on and off"
                    : before ? "unticked and ticked" : "ticked and unticked") +
                " again before anything used it: the flow needs none of it.", "Leave it out", [action.Source.Id], [action.Source.Id]);
        if (index + 1 < actions.Count && actions[index + 1] is var again && action is { Kind: "click" } && again.Kind == "click"
            && again.Key == action.Key && action.Has("changed") && again.Has("changed") && !HasResult(action) && !HasResult(again)
            && action.Checks.Count == 0 && again.Checks.Count == 0 && Toggles(action)
            && (index == 0 || actions[index - 1].Key != action.Key))
            return new Suggestion($"roundTrip:{action.Source.Id}",
                $"{Quote(Names.Human(action.Source.Target)) ?? "The element"} is clicked twice in a row, which turned it on and off again: the flow needs neither.",
                "Leave both out", [action.Source.Id, again.Source.Id], [action.Source.Id, again.Source.Id]);
        // A menu's choice made back: its button says what is chosen, as a language's does.
        if (index + 3 < actions.Count && actions.Skip(index).Take(4).ToList() is [var open, var pick, var reopen, var back]
            && new[] { open, pick, reopen, back }.All(each => each is { Kind: "click", Checks.Count: 0 } && !each.Has("urlChanged") && !each.Has("dialogOpened"))
            && StepNamer.Label(open) is { } first && StepNamer.Label(pick) is { } other && first != other
            && StepNamer.Label(reopen) == other && StepNamer.Label(back) == first
            && pick.Source.Target?.Role is "menuitem" or "option" or "menuitemradio")
            return new Suggestion($"roundTrip:{open.Source.Id}",
                $"“{other}” is chosen, then “{first}” again, as it was, before anything used it: the flow needs none of it.",
                "Leave these 4 out", [open.Source.Id, pick.Source.Id, reopen.Source.Id, back.Source.Id],
                [open.Source.Id, pick.Source.Id, reopen.Source.Id, back.Source.Id]);
        return null;
    }

    // What a second click turns back: a toggle by its words, a menu's group that opens and closes, a switch.
    private static bool Toggles(PlannedAction click) =>
        click.Source.Target?.Role is "switch" or "menuitem" && !click.Has("urlChanged")
        || StepNamer.Label(click) is { } label && ToggleWord().IsMatch(label);

    [GeneratedRegex(@"\b(toggle|dark|light|theme|mode|expand|collapse|show|hide|more|less)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ToggleWord();

    // A choice of a list of several taken back by its "Remove ..." button before anything used it: the flow chooses what
    // was left.
    private static Suggestion? Removed(List<PlannedAction> actions, int index)
    {
        var click = actions[index];
        if (click.Kind != "click" || HasResult(click) || RemoveWord().Match(click.Source.Target?.Text ?? "") is not { Success: true } match)
            return null;
        var removed = match.Groups["value"].Value.Trim();
        var choice = actions.FindLastIndex(index - 1, other => other.Kind == "choose" && other.Source.Value == removed);
        if (choice < 0 || actions.Skip(choice + 1).Take(index - choice - 1).Any(between => between.Checks.Count > 0 || HasResult(between)))
            return null;
        var kept = actions.Take(index).Where(other => other.Kind == "choose" && other.Key == actions[choice].Key && other != actions[choice])
            .Select(other => $"“{other.Source.Value}”").ToList();
        var left = kept.Count == 0 ? "" : $": the choice that stays is {string.Join(", ", kept)}";
        return new Suggestion($"removed:{click.Source.Id}",
            $"“{removed}” is chosen, then removed before anything used it{left}.", "Leave both out",
            [actions[choice].Source.Id, click.Source.Id], [actions[choice].Source.Id, click.Source.Id]);
    }

    /// <summary>
    /// The choice is one of several the field holds at once, as a multi-select's, as the capture script saw; a recording of
    /// an older recorder did not look, so there an autocomplete chosen from again right after is taken for one.
    /// </summary>
    public static bool IsMulti(PlannedAction action, IReadOnlyList<PlannedAction>? around = null)
    {
        if (action.Kind != "choose") return false;
        if (action.Effects.FirstOrDefault(effect => effect is { Kind: "settled", Multi: not null }) is { } settled) return settled.Multi == true;
        if (around is null || action.Source.Widget != "autocomplete") return false;
        var at = around.ToList().IndexOf(action);
        // The next choice, past text typed into the field and clicks that showed nothing, is of the same field.
        bool Again(int step)
        {
            for (var index = at + step; index >= 0 && index < around.Count; index += step)
            {
                var other = around[index];
                if (other.Kind == "choose") return other.Key == action.Key;
                if (other.Kind == "fill" && other.Key == action.Key || other.Kind is "click" && other.Effects.Count == 0) continue;
                return false;
            }

            return false;
        }

        return Again(-1) || Again(1);
    }

    // The text typed was the filter of the list the choice after it was made in.
    private static bool IsSearchField(PlannedAction typed, PlannedAction choice) =>
        typed.Source.Widget == "autocomplete" || choice.Source.Widget == "autocomplete";

    private static string Field(PlannedAction action) =>
        action.Value?.Name is { } name ? StepNamer.Human(name) is var words && words.Length > 0
            ? char.ToUpperInvariant(words[0]) + words[1..]
            : name
        : "The field";

    private static string? Quote(string? text) => string.IsNullOrWhiteSpace(text) ? null : $"“{text.Trim()}”";

    [GeneratedRegex(@"^(?:Remove|Delete|Clear|Unselect|Deselect|Unpick)\s+(?<value>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex RemoveWord();
}
