namespace QaaS.Playwright.Recorder.Generation;

/// <summary>
/// Finds what the person did again and again in a row: the same kinds of actions on the same elements, differing only in
/// the values typed or chosen, the row acted on (by its key) or the checkbox of a group ticked (by its label), and in a
/// locator only by those. A round may go through a dialog it opens and closes, as ticking a row, its trash button, and
/// "Yes, Delete" in the dialog that asks do. Three rounds or more make a loop; two do for the choices of a multi-select.
/// Each loop becomes a step of its own, over a list of what each round typed, chose or acted on (a list of records when
/// a round has several), or a number of times when a round holds none, but only when each round did something the page
/// showed: clicking a button again that did nothing is no loop. Anything else that differs, another element or another
/// action, leaves the actions as they are. A check the person chose after the last round follows the loop; one after any
/// other ends it there.
/// </summary>
internal static class LoopFinder
{
    private const int MinRepeats = 3;

    // The choices of a multi-select are a list however few.
    private const int MinChoices = 2;

    // A value is looked for in a locator when it has this many characters: "2" is part of too many.
    private const int MinValueInLocator = 3;

    private static readonly HashSet<string> Repeatable = ["click", "dblclick", "fill", "check", "uncheck", "choose", "press"];
    private static readonly HashSet<string> Unrepeatable = ["urlChanged", "popup", "jsDialog"];

    // What a button that adds an item of a list starts with: "Add line" adds a Line.
    private static readonly string[] AddingVerbs = ["Add", "Create", "New", "Insert", "Append", "Save", "Submit", "Enter"];

    /// <summary>The loops among the actions: where each starts, its round's length, and how many rounds.</summary>
    public static List<(int Start, int Length, Loop Loop)> Find(List<PlannedAction> actions)
    {
        var loops = new List<(int, int, Loop)>();
        for (var start = 0; start < actions.Count;)
        {
            if (Longest(actions, start) is { } found)
            {
                loops.Add((start, found.Length, Loop(actions, start, found.Length, found.Repeats)));
                start += found.Length * found.Repeats;
            }
            else
                start++;
        }

        return loops;
    }

    /// <summary>The action's row is the one of a value its own repeat typed, so the loop finds it by that value.</summary>
    public static bool RowFromValue(PlannedAction action, IEnumerable<PlannedAction> repeat) =>
        action.RowKey?.Text is { } key && repeat.SelectMany(other => other.Values).Any(value => value.Text == key);

    // The run of repeats from start that covers the most actions; of two that cover as many, the one of fewer actions.
    private static (int Length, int Repeats)? Longest(List<PlannedAction> actions, int start)
    {
        (int Length, int Repeats)? best = null;
        for (var length = 1; start + length * MinChoices <= actions.Count; length++)
        {
            var template = actions.GetRange(start, length);
            if (!CanRepeat(template[^1], mayCheck: true) || !Balanced(template)) continue;
            var repeats = 1;
            while (start + (repeats + 1) * length <= actions.Count && template[^1].Checks.Count == 0)
            {
                var next = actions.GetRange(start + repeats * length, length);
                if (!Same(template, next)) break;
                repeats++;
                if (next[^1].Checks.Count > 0) break;
            }

            var rounds = actions.GetRange(start, length * repeats).Chunk(length).ToList();
            // A round acts on one row, as a person goes through them; a round that ends on the next row is the rounds
            // seen from the wrong start.
            if (rounds[0].Select(action => action.RowKey).OfType<Setting>().Distinct().Count() > 1) continue;
            var least = length == 1 && rounds.All(round => Noise.IsMulti(round[0], actions)) ? MinChoices : MinRepeats;
            if (repeats < least || !HasEvidence(rounds)) continue;
            if (best is null || length * repeats > best.Value.Length * best.Value.Repeats) best = (length, repeats);
        }

        return best;
    }

    // A round that holds no value repeats only what each round showed for itself: a row added, a message, a wait. A click
    // repeated that changed nothing is the person trying again, which a loop would only do again.
    private static bool HasEvidence(List<PlannedAction[]> rounds) =>
        rounds[0].Any(action => LoopValues(action, rounds[0]).Any())
        || rounds.All(round => round.Any(action => action.Effects.Count > 0));

    // Never a secret typed: a list's values are in its YAML, where a secret never is.
    private static bool CanRepeat(PlannedAction action, bool mayCheck = false) =>
        Repeatable.Contains(action.Kind) && action.Locator is not null && (mayCheck || action.Checks.Count == 0)
        && !action.Effects.Any(effect => Unrepeatable.Contains(effect.Kind)) && action.Value?.Secret != true;

    // A round that opens a dialog closes it again, so the next round starts where it started.
    private static bool Balanced(List<PlannedAction> template)
    {
        var open = new List<string?>();
        foreach (var effect in template.SelectMany(action => action.Effects))
        {
            if (effect.Kind == "dialogOpened") open.Add(effect.Dialog?.Title);
            else if (effect.Kind == "dialogClosed" && !open.Remove(effect.Dialog?.Title)) return false;
        }

        return open.Count == 0 && template.All(action => action.Dialog == template[0].Dialog
                                                         || template.Any(opener => Opens(opener, action.Dialog)));
    }

    private static bool Opens(PlannedAction action, string? dialog) =>
        action.Effects.Any(effect => effect.Kind == "dialogOpened" && effect.Dialog?.Title == dialog);

    private static bool Same(List<PlannedAction> template, IReadOnlyList<PlannedAction> repeat)
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
                       && first.Has("dialogOpened") == other.Has("dialogOpened") && first.Has("dialogClosed") == other.Has("dialogClosed")
                       && LoopValues(first, template).Select(value => Shape(first, value))
                           .SequenceEqual(LoopValues(other, repeat).Select(value => Shape(other, value)))
                       && Templated(first, templateValues) == Templated(other, repeatValues);
            if (!same) return false;
        }

        return true;
    }

    // A checkbox of a group is told by its group, as the flag of each has its own label's name.
    private static (string Name, string Kind) Shape(PlannedAction action, Setting value) =>
        value.Flag is not null && action.Member is { } member ? (member.Name + "Ticked", "bool")
        : (value.Name, value.IsNumber ? "number" : value.Type);

    /// <summary>
    /// What a round's action holds that may change from round to round: the values it typed or chose, the key of the row
    /// it acts on unless a value the round typed is that key, and a checkbox's label in its group.
    /// </summary>
    public static IEnumerable<Setting> LoopValues(PlannedAction action, IEnumerable<PlannedAction> round)
    {
        foreach (var value in action.Values) yield return value;
        if (action.RowKey is { } row && !RowFromValue(action, round)) yield return row;
        if (action.Member is { } member) yield return member;
    }

    /// <summary>What a round holds that may change from round to round, each setting once, as two actions on one row read one key.</summary>
    public static List<Setting> ValuesOf(IEnumerable<PlannedAction> actions)
    {
        var round = actions.ToList();
        return [.. round.SelectMany(action => LoopValues(action, round)).Distinct()];
    }

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
                                || (value.Text is { Length: > 0 } && (value.Text == own || value.Text == action.Member?.Text
                                                                      || value.Text == action.RowKey?.Text)))
                .OrderByDescending(value => value.Text!.Length);
            // As a word of its own, as the writer reads a value into a locator: "TradeCode 9" is in no "TradeCode 99".
            foreach (var (value, index) in marked) text = Text.ReplaceWords(text, value!, $"\u0001{index}\u0001");
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
        var rounds = Enumerable.Range(0, repeats).Select(repeat => actions.GetRange(start + repeat * length, length)).ToList();
        var items = rounds.Select(ValuesOf).ToList();

        // A flag every round set alike is no value of the list: the flow ticks or unticks, as each round did.
        var constant = Enumerable.Range(0, items[0].Count)
            .Where(index => items[0][index].Flag is not null && items.All(item => item[index].Flag == items[0][index].Flag))
            .ToHashSet();
        foreach (var round in rounds)
        {
            var values = ValuesOf(round);
            var alike = constant.Select(index => values[index]).ToHashSet();
            foreach (var action in round.Where(action => action.Value is { } flag && alike.Contains(flag))) action.Value = null;
        }

        items = [.. items.Select(item => item.Where((_, index) => !constant.Contains(index)).ToList())];
        var holds = items.SelectMany(item => item).ToHashSet();

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
                Length = length, Repeats = repeats, Holds = holds,
                List = new Setting(ListName(template, items[0][0]), null)
                    { Type = fields[0].Type + "[]", Items = items, Fields = fields },
            };

        // Never a member every record has, as Equals.
        var names = new HashSet<string>(CSharp.RecordMembers, StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            var name = field.Name;
            for (var copy = 2; !names.Add(field.Name); copy++) field.Name = $"{name}{copy}";
        }

        var itemType = ItemName(template[^1]);
        // A choice typed for, each round: its record is the option's, what was typed its Search, the option its Name.
        if (template[^1] is { Kind: "choose", Value: { } chosen } && fields.Count == 2 && fields[0].Name == chosen.Name + "Search")
        {
            itemType = Singular(chosen.Name);
            (fields[0].Name, fields[1].Name) = ("Search", "Name");
        }

        // Each repeat's values are its record's fields, as the panel shows them.
        foreach (var item in items)
            for (var index = 0; index < fields.Count; index++)
                item[index].Name = fields[index].Name;
        if (fields.Any(field => field.Name.Equals(itemType, StringComparison.OrdinalIgnoreCase))) itemType += "Item";
        return new Loop
        {
            Length = length, Repeats = repeats, Holds = holds,
            List = new Setting(Plural(itemType), null) { ItemType = itemType, Items = items, Fields = fields },
        };
    }

    // The list of one value: the plural of its name; of rows acted on, the rows' key and what the round does to them,
    // as "UsernamesToDelete".
    private static string ListName(List<PlannedAction> round, Setting value)
    {
        var plural = Plural(value.Name);
        if (!round.Any(action => action.RowKey == value)) return plural;
        var verb = Verb(round);
        return verb is null ? plural : $"{plural}To{verb}";
    }

    /// <summary>
    /// What a round does, by its button: the first word of a click on its row ("Delete" for a row's trash), else of the
    /// last click's last word when it is a verb ("Yes, Delete"); else what it does to a checkbox.
    /// </summary>
    public static string? Verb(IReadOnlyList<PlannedAction> round)
    {
        foreach (var click in round.Where(action => action is { Kind: "click" or "dblclick", RowKey: not null }))
            if (CSharp.Pascal(StepNamer.Label(click, without: click.RowKey?.Text)) is { } label && StepNamer.IsVerb(FirstWord(label)))
                return FirstWord(label);
        if (round.LastOrDefault(action => action.Kind == "click") is { } last && StepNamer.Label(last) is { } said
            && CSharp.Pascal(said.Split(' ', ',').LastOrDefault(word => word.Length > 0)) is { } lastWord && StepNamer.IsVerb(lastWord))
            return lastWord;
        return round.Any(action => action.Kind == "uncheck") ? "Uncheck" : round.Any(action => action.Kind == "check") ? "Check" : null;
    }

    private static string FirstWord(string pascal) => StepNamer.Words(pascal).FirstOrDefault() ?? pascal;

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

    /// <summary>
    /// The English plural of a PascalCase name's last word: Line gives Lines, Category Categories; a name that already
    /// is one stays: Subjects, Hobbies.
    /// </summary>
    public static string Plural(string name)
    {
        if (IsPlural(name)) return name;
        if (name.EndsWith('y') && name.Length > 1 && !"aeiou".Contains(name[^2])) return name[..^1] + "ies";
        return name.EndsWith('s') || name.EndsWith('x') || name.EndsWith('z') || name.EndsWith("ch", StringComparison.Ordinal)
               || name.EndsWith("sh", StringComparison.Ordinal)
            ? name + "es"
            : name + "s";
    }

    /// <summary>The singular of a PascalCase plural: Hobbies gives Hobby, Subjects Subject, Boxes Box.</summary>
    public static string Singular(string name)
    {
        if (!IsPlural(name)) return name;
        if (name.EndsWith("ies", StringComparison.Ordinal)) return name[..^3] + "y";
        return name.EndsWith("ches", StringComparison.Ordinal) || name.EndsWith("shes", StringComparison.Ordinal)
               || name.EndsWith("xes", StringComparison.Ordinal) || name.EndsWith("sses", StringComparison.Ordinal)
            ? name[..^2]
            : name[..^1];
    }

    // A last word ending in an s that is no part of the word itself: Subjects, Hobbies; not Status, Address, Analysis.
    private static bool IsPlural(string name) =>
        name.Length > 3 && name.EndsWith('s') && !name.EndsWith("ss", StringComparison.Ordinal)
        && !name.EndsWith("us", StringComparison.Ordinal) && !name.EndsWith("is", StringComparison.Ordinal)
        && !name.EndsWith("os", StringComparison.Ordinal);
}
