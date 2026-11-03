using System.Text.RegularExpressions;
using QaaS.Playwright.Recorder.Capture;

namespace QaaS.Playwright.Recorder.Generation;

/// <summary>
/// Names each step after what a person does in it: <c>OpenBookAppointmentAsync</c>, <c>FillBookAppointmentAsync</c>,
/// <c>SaveChangesAsync</c>, <c>AssignTicketAsync</c> for a row's "Assign" button, <c>AddLinesAsync</c> for a loop. A
/// dialog is named by its title when that is English, else by the id of the button that opened it, else by the flow's
/// own name. A name the person gave a step in the panel wins over all of these. Each step also gets a sentence that says
/// what it does, for its doc comment.
/// </summary>
internal static partial class StepNamer
{
    private static readonly string[] ElementSuffixes = ["Button", "Btn", "Link", "MenuItem", "Item", "Tab"];

    // What a link's or a tab's text starts with when it says what it does, rather than where it goes.
    private static readonly HashSet<string> Verbs =
    [
        "Open", "Go", "View", "Show", "Back", "Next", "Previous", "Add", "Create", "New", "Edit", "Delete", "Remove", "Save",
        "Submit", "Cancel", "Close", "Search", "Send", "Assign", "Archive", "Log", "Sign", "Download", "Upload", "Export",
        "Import", "Print", "Apply", "Confirm", "Approve", "Reject", "Select", "Choose", "Clear", "Reset", "Refresh", "Update",
        "Start", "Stop", "Continue", "Finish", "Book", "Schedule", "Pay", "Buy", "Find", "Filter", "Sort", "Copy", "Move",
        "Share", "Invite", "Accept", "Decline", "Enable", "Disable", "Run", "Load", "Return",
    ];

    // What a button that only confirms a choice says.
    private static readonly HashSet<string> Confirms = new(StringComparer.OrdinalIgnoreCase)
        { "Confirm", "OK", "Done", "Apply", "Select", "Choose", "Continue", "Next", "Use" };

    // Words a label may end with once the record's key is taken out of it: "Actions for HL-775-ZE" gives "Actions".
    private static readonly string[] Dangling = ["for", "of", "to", "in", "on", "at", "with", "by", "from", "#"];

    // A summary fits on one line: "    /// <summary>" and "</summary>" around it.
    private const int SummaryWidth = FlowWriter.MaxWidth - 27;

    public static void Name(FlowPlan plan)
    {
        var subjects = new Dictionary<string, string>();
        var used = new HashSet<string>(StringComparer.Ordinal) { "RunAsync", "FileToUpload" };
        var given = plan.Steps.Select(GivenName).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var flowNameTaken = false;

        // A dialog without a title of any kind is told apart by the button that opened it, as every such dialog has the
        // same empty title.
        string DialogSubject(string title, CapturedDialog? info, PlannedAction? opener = null)
        {
            if (title.Length == 0 && opener is not null && OpenerSubject(opener) is { } own) return own;
            if (subjects.TryGetValue(title, out var known)) return known;
            var titleWords = info?.Prefix is { Length: > 0 } prefix ? prefix : title;
            var subject = (CSharp.IsAsciiText(titleWords) ? CSharp.Pascal(titleWords) : null) ?? OpenerStem(plan, title);
            if (subject is null && !flowNameTaken)
            {
                subject = plan.ClassName;
                flowNameTaken = true;
            }

            subject ??= $"Dialog{subjects.Count + 1}";
            subjects[title] = subject;
            return subject;
        }

        foreach (var step in plan.Steps)
        {
            var last = step.Actions[^1];
            var verb = Verb(last);
            var hasValues = step.Actions.Any(action => action.Value is not null || action.Kind == "upload");
            string name;
            if (step.Loop is { } loop)
                name = LoopName(step, loop);
            else if (step.Opens is { } opened)
                name = "Open" + DialogSubject(opened.Title, opened, last);
            else if (step.OpensPage is not null)
                name = verb is null ? "OpenPage" : verb.StartsWith("Open", StringComparison.Ordinal) ? verb : "Open" + verb;
            else if (step.Actions.All(action => action.Kind == "expect"))
                name = "Check" + (step.Dialog is { } title ? DialogSubject(title, last.Source.Dialog) : PageSubject(last.Source.Url) ?? "Page");
            else
            {
                // The section the step works in, by its heading, says more than the page: Personal Details.
                var subject = step.Dialog is { } title ? DialogSubject(title, last.Source.Dialog)
                    : SectionOf(step) ?? PageSubject(last.Source.Url) ?? plan.ClassName;
                name = Choice(step) is { } chosen ? "Choose" + chosen
                    : Toggles(step) is { } toggled ? toggled
                    : verb is not null && (step.Ended || !hasValues) ? verb
                    : last.Kind == "navigate" ? "Open" + (PageSubject(last.Source.Url) ?? "Page")
                    : last is { Kind: "press", Source.Key: "Escape" } && last.Has("dialogClosed") ? "Close" + subject
                    : hasValues ? "Fill" + subject
                    : last.Has("dialogClosed") ? "Submit" + subject
                    : "Use" + subject;
                // A second form step of the same section or page is named after what it fills in.
                if (hasValues && name == "Fill" + subject && used.Contains(name + "Async") && Fields(step) is { } fields) name = "Fill" + fields;
            }

            // A name the person gave is theirs; another step's own name gives way to it. A name taken is the step done
            // again, numbered only from the third time.
            var own = GivenName(step);
            if (own is not null) name = own[..^"Async".Length];
            var unique = name + "Async";
            for (var copy = 2; (own is null && given.Contains(unique)) || !used.Add(unique); copy++)
                unique = copy == 2 ? $"{name}AgainAsync" : $"{name}{copy}Async";
            step.Name = unique;
            step.Summary = Summary(step, plan);
        }
    }

    /// <summary>Whether the word says what a button does: Delete, Save, Add.</summary>
    public static bool IsVerb(string word) => Verbs.Contains(word);

    /// <summary>
    /// The method name the person gave the step, kept by its first action that has one, e.g. <c>SaveTicketAsync</c>:
    /// in PascalCase, ending with <c>Async</c>. Null when they gave none.
    /// </summary>
    public static string? GivenName(Step step) =>
        step.Actions.Select(action => MethodName(action.Source.Edit?.Step)).FirstOrDefault(name => name is not null);

    /// <summary>The text as a step's method name, <c>save ticket</c> giving <c>SaveTicketAsync</c>; null when it has no letter.</summary>
    public static string? MethodName(string? text) =>
        CSharp.Pascal(text) is not { } name || !name.Any(char.IsLetter) ? null
        : name.EndsWith("Async", StringComparison.Ordinal) && name.Length > 5 ? name : name + "Async";

    /// <summary>
    /// The text of the element clicked, as a person reads it, without <paramref name="without"/> (a record's key in
    /// it): "Assign TCK-1041" gives "Assign". Null when nothing is left.
    /// </summary>
    public static string? Label(PlannedAction action, string? without = null)
    {
        // An icon button by what its icon says (a trash is "Delete"), a close button's cross by its word.
        var own = action.Source.Target?.Text;
        var text = own is { Length: > 0 } && CloseGlyph().IsMatch(own) ? "Close"
            : !string.IsNullOrWhiteSpace(own) ? own
            : Names.IconName(action.Source.Target?.Icon);
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (without is { Length: > 0 }) text = text.Replace(without, " ", StringComparison.Ordinal);
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 0
               && (Dangling.Contains(words[^1], StringComparer.OrdinalIgnoreCase) || !words[^1].Any(char.IsLetterOrDigit)))
            words.RemoveAt(words.Count - 1);
        var label = string.Join(' ', words).Trim(' ', ':', '-', ',', '(');
        return label.Any(char.IsLetter) ? label : null;
    }

    // What the clicked element says, when it is English: "Save changes" gives SaveChanges, a link "Orders" OpenOrders,
    // the "Assign" button of a ticket's row AssignTicket. Else its id or test id: "save-btn" gives Save.
    private static string? Verb(PlannedAction action)
    {
        if (action.Kind is not ("click" or "dblclick")) return null;
        var target = action.Source.Target;
        var label = Label(action, without: action.RowKey?.Text);
        var verb = label is { Length: <= 40 } && CSharp.IsAsciiText(label) && !label.Any(char.IsDigit) ? CSharp.Pascal(label)
            : Stem(target);
        if (verb is null) return null;
        // A lone verb says what of by its section: "Add" in Attachments adds an attachment.
        if (Verbs.Contains(verb) && CSharp.IsAsciiText(target?.Section) && CSharp.Pascal(target!.Section) is { } section
            && !verb.Equals(section, StringComparison.Ordinal))
            verb += LoopFinder.Singular(section);
        if (target?.Role is "link" or "tab" && !Verbs.Contains(FirstWord(verb))) verb = "Open" + verb;
        if (action.RowKey is not null && CSharp.Pascal(target?.Row?.Header) is { } header
                                      && !verb.Contains(header, StringComparison.Ordinal))
            verb += header;
        return verb;
    }

    // A step that only ticks or turns on: by its setting when that says what it does (SelectAll), else CheckTerms,
    // TurnOnNotifications.
    private static string? Toggles(Step step)
    {
        if (step.Actions is not [{ Kind: "check" or "uncheck", Value.Name: { } setting } only] || only.Source.Widget == "radio") return null;
        if (IsVerb(FirstWord(setting))) return setting;
        return (only.Source.Widget == "switch" ? only.Kind == "check" ? "TurnOn" : "TurnOff" : only.Kind == "check" ? "Check" : "Uncheck") + setting;
    }

    // The section all of a step's actions are in, by its heading.
    private static string? SectionOf(Step step) =>
        step.Actions.Select(action => action.Source.Element?.Section).Distinct().ToList() is [{ } section] && CSharp.IsAsciiText(section)
            ? CSharp.Pascal(section)
            : null;

    // What a form step fills in, by its first two settings: GenderAndEmail.
    private static string? Fields(Step step)
    {
        var names = step.Actions.Select(action => action.Value?.Name).OfType<string>().Distinct().Take(2).ToList();
        return names.Count == 0 ? null : string.Join("And", names);
    }

    // What a step chooses, when choosing is all it does, maybe confirmed by a click on "Confirm", "OK" and the like: the
    // setting of its last choice (Shipping), or of its two (StateAndCity), else the label of the option or card it
    // clicked last (Express).
    private static string? Choice(Step step)
    {
        var actions = step.Actions.ToList();
        var confirmed = actions is [_, .., { Kind: "click" } confirm] && Label(confirm) is { } said && Confirms.Contains(said);
        if (confirmed) actions.RemoveAt(actions.Count - 1);
        if (actions.Count == 0 || actions.Any(action => action.Kind is not ("choose" or "click"))) return null;
        var last = actions[^1];
        // A setting numbered to tell it from another of its field is the same field: City2 is the city chosen again.
        static string? Field(PlannedAction choice) => choice.Value?.Name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9') is { Length: > 0 } name ? name : choice.Value?.Name;
        if (actions.Where(action => action.Kind == "choose").Select(Field).Distinct().ToList() is [{ } one, { } two]
            && actions.All(action => action.Kind == "choose"))
            return $"{one}And{two}";
        if (last.Kind == "choose") return Field(last);
        // An item of a list without a role of its own; a link or menu item of one when a click confirms it.
        var item = last.Source.Target is { Item: true, Role: null or "option" or "listitem" }
                   || (confirmed && last.Source.Target is { Item: true, Role: "link" or "menuitem" });
        // One that goes to another address is a way there, not a choice.
        if (!item || last.Has("urlChanged") || last.Has("popup")) return null;
        // Its words before a number, as "Express" of "Express 2 days"; one not in English is an option still.
        var words = Label(last)?.Split(' ').TakeWhile(word => !word.Any(char.IsDigit)).ToList() ?? [];
        var label = string.Join(' ', words);
        return label.Length is > 0 and <= 40 && CSharp.IsAsciiText(label) ? CSharp.Pascal(label) : "Option";
    }

    // A loop by what each round does to its list: CheckHobbiesAsync, ChooseSubjectsAsync, DeleteUsernamesAsync,
    // AddLinesAsync; one of no list by its button: AddEmptyLinesAsync.
    private static string LoopName(Step step, Loop loop)
    {
        var round = step.Actions.Take(loop.Length).ToList();
        var last = round[^1];
        var label = last.Kind is "click" or "dblclick" && Label(last) is { } text && CSharp.IsAsciiText(text)
                    && !text.Any(char.IsDigit)
            ? CSharp.Pascal(text)
            : null;
        if (loop.List is not { } list)
            return label is null ? "Repeat" : LoopFinder.Plural(label);
        var items = ListSubject(list.Name);
        if (round.Any(action => action.RowKey is not null && loop.Holds.Contains(action.RowKey)) && LoopFinder.Verb(round) is { } verb)
            return verb + items;
        if (round.All(action => action.Kind is "check" or "uncheck" or "choose" or "fill" or "press"))
            return (round[0].Kind switch
            {
                "check" when round[0].Source.Widget == "radio" => "Pick",
                "check" => "Check",
                "uncheck" => "Uncheck",
                "choose" => "Choose",
                _ when last.Kind == "press" => "Enter",
                _ => "Fill",
            }) + items;
        if (label is null) return "Fill" + items;
        var first = FirstWord(label);
        return label.Length > first.Length ? first + LoopFinder.Plural(label[first.Length..]) : label + items;
    }

    // A list's name without what is done to it: UsernamesToDelete gives Usernames.
    private static string ListSubject(string list) =>
        list.IndexOf("To", StringComparison.Ordinal) is var at and > 0 && at + 2 < list.Length && char.IsUpper(list[at + 2]) ? list[..at] : list;

    private static string FirstWord(string pascal) => FirstWordOf().Match(pascal).Value;

    // What the button that opened a dialog says, or its id or test id when that says more: a button "Change" with the
    // id "change-shipping-button" gives ChangeShipping.
    private static string? OpenerSubject(PlannedAction opener)
    {
        if (opener.Kind is not ("click" or "dblclick")) return null;
        var stem = Stem(opener.Source.Target);
        var label = Label(opener) is { Length: <= 40 } text && CSharp.IsAsciiText(text) && !text.Any(char.IsDigit)
            ? CSharp.Pascal(text)
            : null;
        return label is not null && stem?.StartsWith(label, StringComparison.Ordinal) == true ? stem : label ?? stem;
    }

    // The button that opened the dialog, by its id or test id: "bookAppointmentButton" gives BookAppointment.
    private static string? OpenerStem(FlowPlan plan, string title)
    {
        var opener = plan.Steps.SelectMany(step => step.Actions).FirstOrDefault(action =>
            action.Effects.Any(effect => effect.Kind == "dialogOpened" && effect.Dialog?.Title == title));
        return Stem(opener?.Source.Target);
    }

    private static string? Stem(CapturedTarget? target)
    {
        var id = new[] { target?.Id, target?.TestId }.FirstOrDefault(value => !UnstableId.IsUnstable(value));
        var stem = CSharp.Pascal(id);
        if (stem is null) return null;
        foreach (var suffix in ElementSuffixes)
            if (stem.Length > suffix.Length && stem.EndsWith(suffix, StringComparison.Ordinal))
                return stem[..^suffix.Length];
        return stem;
    }

    // The page's last path segment that is no record's id, nor the name of one ("empNumber/7"), without a verb a route
    // starts with: "/vehicles/VH-1001/notes" gives Notes, "/pim/viewPersonalDetails/empNumber/7" PersonalDetails.
    private static string? PageSubject(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        bool IsId(string part) => part.Any(char.IsDigit) || (part.Length > 12 && UnstableId.IsGuidLike(part));
        var segment = segments.Select((part, index) => (part, index))
            .LastOrDefault(found => !IsId(found.part) && CSharp.IsAsciiText(found.part) && !found.part.Contains('.')
                                    && !(found.index + 1 < segments.Length && IsId(segments[found.index + 1]) && KeyName().IsMatch(found.part))).part;
        if (CSharp.Pascal(segment) is not { } pascal) return null;
        var words = Words(pascal).ToList();
        return words.Count > 1 && RouteVerbs.Contains(words[0]) ? string.Concat(words.Skip(1)) : pascal;
    }

    // What a route's last segment starts with that says what the page does with what it names: viewPersonalDetails.
    private static readonly HashSet<string> RouteVerbs = new(StringComparer.OrdinalIgnoreCase)
        { "View", "Show", "Edit", "Get", "List", "Manage", "Display", "Index", "Save", "Update" };

    // ── What a step does ─────────────────────────────────────────────────────────────────────────────────────────

    // The first that fits on one line: in full, then with what the page showed said shorter, then without it.
    private static string Summary(Step step, FlowPlan plan)
    {
        var values = plan.Settings.Concat(step.Actions.SelectMany(action => action.Values.Append(action.RowKey)))
            .Select(setting => setting?.Text).OfType<string>().Where(text => text.Length >= 2)
            .Distinct().OrderByDescending(text => text.Length).ToList();
        var full = new Describer(step, values, plan.StartUrl, Short: false).Describe(outcome: true);
        if (full.Length <= SummaryWidth) return full;
        var shorter = new Describer(step, values, plan.StartUrl, Short: true);
        return shorter.Describe(outcome: true) is { Length: <= SummaryWidth } said ? said : shorter.Describe(outcome: false);
    }

    private sealed record Describer(Step Step, IReadOnlyList<string> Values, string StartUrl, bool Short)
    {
        public string Describe(bool outcome)
        {
            var step = Step;
            if (step.Opens is { } opened) return $"Opens the {Quote(opened.Title) ?? "new"} dialog.";
            var last = step.Actions[^1];
            if (last.Kind == "newTab")
                return Address(last.Source.Url) is { } address ? $"Opens a new tab at {address}." : "Opens a new tab.";
            if (step.Loop is { List: { } list } loop)
            {
                var round = step.Actions.Take(loop.Length).ToList();
                var items = Human(list.Name);
                return round is [{ Kind: "check" or "uncheck" or "choose" or "fill" } only]
                    ? Sentence($"{VerbOf(only)} each of the {items}")
                    : Sentence($"for each of the {items}, {Join(Phrases(round))}");
            }

            if (step.Loop is { Count: { } count } repeated)
                return Sentence($"{Join(Phrases(step.Actions.Take(repeated.Length)))} {count.Name} times");
            var said = Join(Phrases(step.Actions));
            if (step.OpensPage is not null) return Sentence($"{said}, which opens a pop-up");
            return Sentence(said + (outcome ? Outcome(last) : ""));
        }

        private static string VerbOf(PlannedAction action) => action.Kind switch
        {
            "check" when action.Source.Widget == "switch" => "turns on",
            "uncheck" when action.Source.Widget == "switch" => "turns off",
            "check" when action.Source.Widget == "radio" => "picks",
            "check" => "ticks",
            "uncheck" => "unticks",
            "choose" => "chooses",
            _ => "fills in",
        };

        // What the actions do, values filled in one after the other said at once: "fills in the title and priority",
        // or "ticks "Urgent"" when they are checkboxes only. Enter pressed in a field just filled in is said with them:
        // "fills in the name and email, pressing Enter after the email".
        private IEnumerable<string> Phrases(IEnumerable<PlannedAction> actions)
        {
            var filled = new List<PlannedAction>();
            var pressed = new List<PlannedAction>();
            var checking = false;
            foreach (var action in actions)
            {
                // Checks one after the other are said once.
                if (action.Kind == "expect" && checking) continue;
                checking = action.Kind == "expect";
                if (action.Value is not null || action.Kind == "upload")
                {
                    filled.Add(action);
                    continue;
                }

                // Only among several fields: of one, "then presses Enter" says it.
                if (action.Kind == "press" && filled.LastOrDefault() is { } field && field.Key == action.Key
                    && actions.Count(other => other.Value is not null) > 1)
                {
                    pressed.Add(action);
                    continue;
                }

                if (filled.Count > 0) yield return Fills(filled, pressed);
                filled.Clear();
                pressed.Clear();
                yield return action.Kind switch
                {
                    "click" or "dblclick" => Clicks(action),
                    "press" => $"presses {action.Source.Key ?? "Enter"}",
                    "navigate" => $"goes to {Address(action.Source.Url) ?? "an address"}",
                    "drag" => "drags and drops",
                    "expect" => "checks what the page shows",
                    "check" or "uncheck" => $"{VerbOf(action)} {Quote(Names.Human(action.Source.Target)) ?? (action.RowKey is null ? "the checkbox" : "its row")}",
                    _ => action.Kind,
                };
            }

            if (filled.Count > 0) yield return Fills(filled, pressed);
        }

        private string Fills(List<PlannedAction> filled, List<PlannedAction> pressed)
        {
            if (filled.Select(action => action.Kind).Distinct().ToList() is [var kind and ("check" or "uncheck")]
                && filled.All(action => action.Source.Widget != "radio"))
            {
                var boxes = filled.Select(action => Quote(Names.Human(action.Source.Target)) ?? Human(action.Value!.Name)).Distinct();
                var switches = filled.All(action => action.Source.Widget == "switch");
                return $"{(switches ? kind == "check" ? "turns on" : "turns off" : kind == "check" ? "ticks" : "unticks")} {Join([.. boxes], " and ")}";
            }

            if (filled.All(action => action.Kind == "choose"))
                return $"chooses the {Join([.. filled.Select(FieldOf).Distinct()], " and ")}";

            // A date typed relative to today is the date still.
            var fields = filled.Select(FieldOf).Distinct().ToList();
            var presses = pressed.Select(press => (press.Source.Key ?? "Enter", FieldOf(filled.Last(field => field.Key == press.Key))))
                .Distinct().ToList();
            var after = presses.Count == 0 ? ""
                : ", pressing " + Join([.. presses.GroupBy(press => press.Item1)
                    .Select(group => $"{group.Key} after the {Join([.. group.Select(press => press.Item2)], " and ")}")], " and ");
            return $"fills in the {Join(fields, " and ")}{after}";
        }

        private static string FieldOf(PlannedAction action) => action.Kind == "upload" ? "file"
            : Human(action.Value!.DateFormat is null ? action.Value.Name : RelativeDate.Member(action.Value.Name));

        private string Clicks(PlannedAction action)
        {
            var target = action.Source.Target;
            var label = Quote(Names.Human(target));
            if (label is null) return $"clicks the {target?.Role ?? "element"}";
            var inRow = !Short && action.RowKey is { Text: { } key }
                               && target?.Text?.Contains(key, StringComparison.Ordinal) == false
                               && CSharp.IsAsciiText(target.Row?.Header)
                ? $" in the {target.Row!.Header!.ToLowerInvariant()}'s row"
                : "";
            return $"clicks {label}{inRow}";
        }

        private string Outcome(PlannedAction action)
        {
            var results = new List<string>();
            var closed = action.Effects.Where(effect => effect.Kind == "dialogClosed").Select(effect => effect.Dialog?.Title);
            foreach (var title in closed.Distinct())
                results.Add(title == Step.Dialog ? "closes the dialog" : $"closes {Quote(title) ?? "a dialog"}");
            if (action.Effects.LastOrDefault(effect => effect.Kind == "urlChanged")?.Url is { } url && Route(url) is { } route)
                results.Add($"goes to {route}");
            if (action.Effects.FirstOrDefault(effect => effect is { Kind: "alert", Text.Length: > 0 }) is { } alert)
                results.Add(Short || Quote(alert.Text) is not { Length: <= 40 } said ? "shows an alert" : $"shows {said}");
            if (results.Count > 0) return $", which {Join(results, " and ")}";
            return action.Has("busy") ? " and waits for it to load" : "";
        }

        // An address the person went to: its path on the site recorded, else all of it.
        private string? Address(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && Uri.TryCreate(StartUrl, UriKind.Absolute, out var start)
                                                              && uri.Authority != start.Authority
                ? url
                : Route(url);

        // The text in quotes, each recorded value and number in it as "...", as they change from case to case.
        private string? Quote(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            foreach (var value in Values) text = text.Replace(value, "\u0001", StringComparison.Ordinal);
            text = NumberWord().Replace(text, "\u0001");
            text = Placeholders().Replace(text, "...").Trim();
            return text.Trim('.', ' ').Length == 0 ? null : $"\"{text}\"";
        }
    }

    private static string Sentence(string text) => char.ToUpperInvariant(text[0]) + text[1..] + ".";

    private static string Join(IEnumerable<string> phrases)
    {
        var list = phrases.ToList();
        return list.Count <= 1 ? string.Concat(list) : string.Join(", ", list[..^1]) + ", then " + list[^1];
    }

    private static string Join(List<string> items, string last) =>
        items.Count <= 1 ? string.Concat(items) : string.Join(", ", items[..^1]) + $" {last.Trim()} " + items[^1];

    /// <summary>A setting's name as words: EstimateHours gives "estimate hours".</summary>
    public static string Human(string name) => string.Join(' ', Words(name).Select(word => word.ToLowerInvariant()));

    /// <summary>The words of a PascalCase name: EstimateHours gives Estimate and Hours.</summary>
    public static IEnumerable<string> Words(string name) => WordOf().Matches(name).Select(word => word.Value);

    /// <summary>The address as the flow checks it: its path, with a record's id in it as [id], as any id matches there.</summary>
    public static string? Route(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var route = uri.Fragment.StartsWith("#/", StringComparison.Ordinal) ? uri.AbsolutePath + uri.Fragment : uri.AbsolutePath;
        return string.Join('/', route.Split('/').Select(segment =>
            segment.Any(char.IsDigit) || (segment.Length > 12 && UnstableId.IsGuidLike(segment))
                ? "[id]"
                : Uri.UnescapeDataString(segment)));
    }

    [GeneratedRegex("^[A-Z][a-z0-9]*")]
    private static partial Regex FirstWordOf();

    [GeneratedRegex(@"^\s*[×✕✖✗xX]\s*$")]
    private static partial Regex CloseGlyph();

    // The name of a record's id in a route: empNumber, userId, id.
    [GeneratedRegex("(?i)(id|number|no|num|key|code)$")]
    private static partial Regex KeyName();

    [GeneratedRegex("[A-Z]+(?![a-z])|[A-Z]?[a-z0-9]+")]
    private static partial Regex WordOf();

    [GeneratedRegex(@"[^\s\u0001]*\d[^\s\u0001]*")]
    private static partial Regex NumberWord();

    [GeneratedRegex(@"\u0001(?:[\s\u0001]*\u0001)?")]
    private static partial Regex Placeholders();
}
