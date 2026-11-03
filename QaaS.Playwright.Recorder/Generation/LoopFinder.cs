namespace QaaS.Playwright.Recorder.Generation;

/// <summary>
/// Finds what the person did three times or more in a row: the same kinds of actions on the same elements, differing
/// only in the values typed or chosen, and in a locator only by a value its own repeat typed (the row of the item just
/// added). Each such run becomes a step of its own that loops, over a list of the values each repeat typed (a list of
/// records when a repeat typed several), or a number of times when the repeats typed none. Anything else that differs,
/// another element or another action, leaves the actions as they are. A check the person chose after the last repeat
/// follows the loop; one after any other ends it there.
/// </summary>
internal static class LoopFinder
{
    private const int MinRepeats = 3;

    // A value is looked for in a locator when it has this many characters: "2" is part of too many.
    private const int MinValueInLocator = 3;

    private static readonly HashSet<string> Repeatable = ["click", "dblclick", "fill", "check", "uncheck", "choose", "press"];
    private static readonly HashSet<string> Unrepeatable = ["dialogOpened", "dialogClosed", "urlChanged", "popup", "jsDialog"];

    // What a button that adds an item of a list starts with: "Add line" adds a Line.
    private static readonly string[] AddingVerbs = ["Add", "Create", "New", "Insert", "Append", "Save", "Submit", "Enter"];

    public static void Fold(FlowPlan plan)
    {
        var steps = plan.Steps.SelectMany(Split).ToList();
        plan.Steps.Clear();
        plan.Steps.AddRange(steps);
    }

    /// <summary>The action's row is the one of a value its own repeat typed, so the loop finds it by that value.</summary>
    public static bool RowFromValue(PlannedAction action, IEnumerable<PlannedAction> repeat) =>
        action.RowKey?.Text is { } key && repeat.SelectMany(other => other.Values).Any(value => value.Text == key);

    private static IEnumerable<Step> Split(Step step)
    {
        var actions = step.Actions;
        var loops = new List<(int Start, int Length, int Repeats)>();
        for (var start = 0; start < actions.Count;)
        {
            if (Longest(actions, start) is { } loop)
            {
                loops.Add((start, loop.Length, loop.Repeats));
                start += loop.Length * loop.Repeats;
            }
            else
                start++;
        }

        if (loops.Count == 0)
        {
            yield return step;
            yield break;
        }

        var at = 0;
        foreach (var (start, length, repeats) in loops)
        {
            if (start > at) yield return Part(step, at, start, ended: false);
            var looped = Part(step, start, start + length * repeats, ended: false);
            looped.Loop = Loop(actions, start, length, repeats);
            yield return looped;
            at = start + length * repeats;
        }

        if (at < actions.Count) yield return Part(step, at, actions.Count, step.Ended);
    }

    private static Step Part(Step step, int from, int to, bool ended)
    {
        var part = new Step { Page = step.Page, Dialog = step.Dialog, Ended = ended };
        part.Actions.AddRange(step.Actions.GetRange(from, to - from));
        return part;
    }

    // The run of repeats from start that covers the most actions; of two that cover as many, the one of fewer actions.
    private static (int Length, int Repeats)? Longest(List<PlannedAction> actions, int start)
    {
        (int Length, int Repeats)? best = null;
        for (var length = 1; start + length * MinRepeats <= actions.Count; length++)
        {
            var template = actions.GetRange(start, length);
            if (!CanRepeat(template[^1])) break;
            var repeats = 1;
            while (start + (repeats + 1) * length <= actions.Count)
            {
                var next = actions.GetRange(start + repeats * length, length);
                if (!Same(template, next)) break;
                repeats++;
                if (next[^1].Checks.Count > 0) break;
            }

            if (repeats >= MinRepeats && (best is null || length * repeats > best.Value.Length * best.Value.Repeats))
                best = (length, repeats);
        }

        return best;
    }

    private static bool CanRepeat(PlannedAction action, bool mayCheck = false) =>
        Repeatable.Contains(action.Kind) && action.Locator is not null && (mayCheck || action.Checks.Count == 0)
        && !action.Effects.Any(effect => Unrepeatable.Contains(effect.Kind));

    private static bool Same(List<PlannedAction> template, List<PlannedAction> repeat)
    {
        var templateValues = ValuesOf(template);
        var repeatValues = ValuesOf(repeat);
        if (templateValues.Count != repeatValues.Count) return false;
        for (var index = 0; index < template.Count; index++)
        {
            var (first, other) = (template[index], repeat[index]);
            var same = CanRepeat(other, mayCheck: index == template.Count - 1) && first.Kind == other.Kind && first.Source.Widget == other.Source.Widget
                       && first.Dialog == other.Dialog && first.Source.Page == other.Source.Page
                       && first.Source.Key == other.Source.Key && first.Source.OnPage == other.Source.OnPage
                       && first.Source.HoverMenu == other.Source.HoverMenu && first.Has("tabSelected") == other.Has("tabSelected")
                       && first.Values.Select(Shape).SequenceEqual(other.Values.Select(Shape))
                       && Templated(first, templateValues) == Templated(other, repeatValues);
            if (!same) return false;
        }

        return true;
    }

    private static (string Name, string Kind) Shape(Setting value) => (value.Name, value.IsNumber ? "number" : value.Type);

    private static List<Setting> ValuesOf(IEnumerable<PlannedAction> actions) => [.. actions.SelectMany(action => action.Values)];

    // The locator with each of the repeat's values in it marked by its place among them, so that two repeats compare equal
    // when their locators differ only by their own values. Only where the flow can put the loop's value back: a role's
    // name, a row's text, a radio's value.
    private static string Templated(PlannedAction action, List<Setting> values)
    {
        if (action.Locator is not { Kind: not "generator" } locator) return action.Key;
        var own = action.Source.Widget == "radio" ? action.Value?.Text : null;
        string Mark(string text)
        {
            var marked = values.Select((value, index) => (value.Text, Index: index))
                .Where(value => value.Text is { Length: >= MinValueInLocator }
                                || (value.Text is { Length: > 0 } && value.Text == own))
                .OrderByDescending(value => value.Text!.Length);
            foreach (var (value, index) in marked) text = text.Replace(value!, $"\u0001{index}\u0001", StringComparison.Ordinal);
            return text;
        }

        return LocatorPolicy.Render("page", locator.Parts, part => part switch
        {
            { M: "role", Name: { } name } => CSharp.Literal(Mark(name)),
            { M: "filter", HasText: { } text } => CSharp.Literal(Mark(text)),
            { M: "css", Css: { } css } when locator.Kind == "nameValue" && own is not null
                                             && css.EndsWith(LocatorPolicy.CssValue(own), StringComparison.Ordinal) =>
                CSharp.Literal(css[..^LocatorPolicy.CssValue(own).Length] + Mark(own)),
            _ => null,
        });
    }

    private static Loop Loop(List<PlannedAction> actions, int start, int length, int repeats)
    {
        var template = actions.GetRange(start, length);
        var items = Enumerable.Range(0, repeats).Select(repeat => ValuesOf(actions.GetRange(start + repeat * length, length)))
            .ToList();
        if (items[0].Count == 0)
            return new Loop
            {
                Length = length, Repeats = repeats,
                Count = new Setting(CountName(template[^1]), CSharp.Number(repeats)) { Type = "int" },
            };

        var fields = items[0].Select((value, index) => new Setting(value.Name, null)
        {
            Type = items.Select(item => item[index].Type).Distinct().ToList() is [var one] ? one : "decimal",
        }).ToList();
        if (fields.Count == 1)
            return new Loop
            {
                Length = length, Repeats = repeats,
                List = new Setting(Plural(fields[0].Name), null) { Type = fields[0].Type + "[]", Items = items, Fields = fields },
            };

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            var name = field.Name;
            for (var copy = 2; !names.Add(field.Name); copy++) field.Name = $"{name}{copy}";
        }

        // Each repeat's values are its record's fields, as the panel shows them.
        foreach (var item in items)
            for (var index = 0; index < fields.Count; index++)
                item[index].Name = fields[index].Name;
        var itemType = ItemName(template[^1]);
        if (names.Contains(itemType)) itemType += "Item";
        return new Loop
        {
            Length = length, Repeats = repeats,
            List = new Setting(Plural(itemType), null) { ItemType = itemType, Items = items, Fields = fields },
        };
    }

    // What the repeat adds, by the button that ends it: "Add line" adds a Line.
    private static string ItemName(PlannedAction last)
    {
        var label = last.Kind is "click" or "dblclick" ? CSharp.Pascal(StepNamer.Label(last)) : null;
        return label is null ? "Item" : WithoutAddingVerb(label) ?? "Item";
    }

    // How many times the person clicked: "Add empty line" gives EmptyLineCount.
    private static string CountName(PlannedAction action)
    {
        var label = action.Kind == "press" ? CSharp.Pascal(action.Source.Key) : CSharp.Pascal(StepNamer.Label(action));
        return (label is null ? "Repeat" : WithoutAddingVerb(label) ?? label) + "Count";
    }

    private static string? WithoutAddingVerb(string pascal)
    {
        foreach (var verb in AddingVerbs)
            if (pascal.Length > verb.Length && pascal.StartsWith(verb, StringComparison.Ordinal)
                                             && char.IsUpper(pascal[verb.Length]))
                return pascal[verb.Length..];
        return null;
    }

    /// <summary>The English plural of a PascalCase name's last word: Line gives Lines, Category Categories.</summary>
    public static string Plural(string name)
    {
        if (name.EndsWith('y') && name.Length > 1 && !"aeiou".Contains(name[^2])) return name[..^1] + "ies";
        return name.EndsWith('s') || name.EndsWith('x') || name.EndsWith('z') || name.EndsWith("ch", StringComparison.Ordinal)
               || name.EndsWith("sh", StringComparison.Ordinal)
            ? name + "es"
            : name + "s";
    }
}
