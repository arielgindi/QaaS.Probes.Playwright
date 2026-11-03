using System.Text.RegularExpressions;
using QaaS.Playwright.Recorder.Capture;

namespace QaaS.Playwright.Recorder.Generation;

/// <summary>
/// What a person calls an element and the setting of its value: its label, or the label beside it in its form group, row
/// or column, the id its developer gave it, the widget it is part of; never a placeholder that is only an example of a
/// value (<c>name@example.com</c>), nor the value a field shows, nor an id a library numbers.
/// </summary>
internal static partial class Names
{
    private const string MuiSelectId = "mui-component-select-";

    // Words an id has for the kind of element it is, not for what it holds: subjectsInput holds the subjects.
    private static readonly HashSet<string> KindWords = new(StringComparer.OrdinalIgnoreCase)
            {
        "Input", "Field", "Box", "Control", "Container", "Wrapper", "Select", "Picker", "Dropdown", "Combo", "Textbox", "Txt", "Inp",
        "Ctrl", "Checkbox", "Check", "Chk", "Cb", "Radio", "Toggle", "Switch",
    };

    // Words a placeholder or a select's prompt says to the person, not what the field is: "Select State", "Type comment here".
    private static readonly HashSet<string> PromptWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Select", "Choose", "Pick", "Enter", "Type", "Write", "Add", "Search", "Find", "Your", "A", "An", "The", "Here", "Please",
        "In", "For", "Hints", "Text", "Of", "To",
    };

    /// <summary>
    /// The element as a person names it: its own label, its accessible name unless only an example placeholder gives it
    /// one, else the label beside it. Null when it has none, as an icon button may.
    /// </summary>
    public static string? Human(CapturedTarget? target)
    {
        if (target is null) return null;
        var text = Clean(target.Text) is { } own && !LocatorPolicy.IsJunk(own) ? LocatorPolicy.WithoutIcon(own) : null;
        var fromPlaceholder = text is not null && (text == Clean(target.Placeholder) || IsExample(text));
        var control = target.Tag is "button" or "a" || target.Role is "button" or "link" or "menuitem" or "tab" or "option";
        return Clean(target.Label)
               ?? (fromPlaceholder ? null : text)
               ?? (control ? null : Clean(Hintless(target.GroupLabel)))
               ?? (text is not null && !IsExample(text) ? text : null)
               ?? IconName(target.Icon);
    }

    /// <summary>
    /// The setting of the value a field holds, as its form names it: a Select's hidden input, its name, its own stable id
    /// (by its label when the label says the same in fewer words: userEmail and "Email" give Email), its test id, the
    /// stable id of the widget it is part of (<c>#state</c>), its label or the one beside it, what its prompt says ("Select
    /// State"); a checkbox by its label. Else "Value".
    /// </summary>
    public static string Setting(CapturedTarget? field)
    {
        if (field is null) return "Value";
        var select = field.Candidates.FirstOrDefault(candidate => candidate.Kind == "select")?.Value;
        var groupMember = field.Role == "checkbox" && field.Candidates.Any(candidate => candidate is { Kind: "name", Count: > 1 });
        // MUI names a Select's box after its field: mui-component-select-shipping.
        var id = field.Id is { } own && own.StartsWith(MuiSelectId, StringComparison.Ordinal) && own.Length > MuiSelectId.Length
            ? own[MuiSelectId.Length..]
            : field.Id;
        var label = English(field.Label) ?? English(Hintless(field.GroupLabel));
        var toggle = field.Role is "checkbox" or "switch" or "radio";

        // A checkbox by its label, as an id numbers it: hobbies-checkbox-3 is Music.
        if (toggle && field.Role != "radio" && English(field.Text) is { } boxLabel && !groupMember
            && (UnstableId.IsUnstable(id) || NumberedId().IsMatch(id!)) && field.Name is null)
            return boxLabel;

        var path = new[] { select, field.Name, id, field.TestId }.FirstOrDefault(value => !UnstableId.IsUnstable(value));
        if (path is not null)
        {
            var name = WithoutKindWords(CSharp.Pascal(path)!);
            if (label is not null && path != field.Name && path != select && Within(label, name)) name = label;
            if (groupMember && path == field.Name) name += CSharp.Pascal(field.Value) ?? CSharp.Pascal(field.Text) ?? "";
            return name;
        }

        // The widget the field is part of: the input of react-select's #state is the state.
        if (field.Candidates.FirstOrDefault(candidate => candidate.Kind == "within" && !UnstableId.IsUnstable(candidate.Value)) is { } within)
            return WithoutKindWords(CSharp.Pascal(within.Value)!);
        if (label is not null) return label;
        // An option chosen with no field of its own is no name for the choice: the next choice changes it.
        if (field.Role == "option") return "Value";
        if (toggle && English(field.Text) is { } toggleLabel) return toggleLabel;
        if (toggle && InColumnHeader(field)) return "SelectAll";
        // A row's checkbox, of no label: the row selected, by its key's column.
        if (toggle && field.Row is { } row) return (CSharp.IsAsciiText(row.Header) ? CSharp.Pascal(row.Header) : "Row") + "Selected";
        if (Prompt(field.Placeholder ?? field.Text) is { } prompt)
            return field.Section is { } section && English(LoopFinder.Singular(CSharp.Pascal(section) ?? "")) is { } part
                                                && StepNamer.Words(prompt).Count() == 1 && !prompt.StartsWith(part, StringComparison.Ordinal)
                ? part + prompt
                : prompt;
        return English(field.Text) is { } text && !IsExample(field.Text!) ? text : "Value";
    }

    /// <summary>
    /// The group a checkbox is one of, in the singular, as a loop over the group's choices names each: the label of the
    /// group ("Hobbies" gives Hobby), the name the checkboxes share, or the stem of their numbered ids
    /// (<c>hobbies-checkbox-3</c>). Null for a checkbox of its own, or one without a label to tell it by.
    /// </summary>
    public static string? Group(CapturedTarget? box)
    {
        if (box is not { Role: "checkbox" } || English(box.Text) is null) return null;
        var shared = box.Candidates.Any(candidate => candidate is { Kind: "name", Count: > 1 }) ? box.Name : null;
        var stem = box.Id is { } id && NumberedId().IsMatch(id) ? NumberedId().Replace(id, "") : null;
        var group = English(Hintless(box.GroupLabel)) ?? CSharp.Pascal(shared) ?? (stem is null ? null : CSharp.Pascal(stem));
        return group is null ? null : LoopFinder.Singular(WithoutKindWords(group));
    }

    /// <summary>
    /// The setting of a file to upload: its field's label without what it says to the person ("Select File" gives File),
    /// in the section it is in when that is all (AttachmentFile); else FileToUpload.
    /// </summary>
    public static string Upload(CapturedTarget? field)
    {
        var said = Prompt(Hintless(field?.Label ?? field?.GroupLabel));
        if (said is null) return "FileToUpload";
        return said is "File" or "Files" or "Document" or "Attachment" && CSharp.Pascal(field?.Section) is { } section
               && CSharp.IsAsciiText(field!.Section) && LoopFinder.Singular(section) is var part && part != said
            ? part + said
            : said;
    }

    /// <summary>The name an icon gives the button it is all of: a trash is "Delete", a pencil "Edit".</summary>
    public static string? IconName(string? icon) => icon?.ToLowerInvariant() switch
    {
        null or "" => null,
        "trash" or "delete" or "bin" or "remove" or "trash-can" or "trash-alt" => "Delete",
        "pencil" or "edit" or "pen" or "pencil-square" or "pencil-alt" or "pen-to-square" => "Edit",
        "plus" or "add" or "plus-circle" or "plus-lg" => "Add",
        "x" or "close" or "times" or "x-lg" or "xmark" or "clear" => "Close",
        "search" or "magnifying-glass" or "zoom" => "Search",
        "question" or "help" or "question-circle" or "question-lg" or "info" => "Help",
        "heart" or "like" or "thumbs-up" or "favorite" => "Like",
        "gear" or "cog" or "settings" => "Settings",
        "download" => "Download",
        "upload" => "Upload",
        "eye" or "view" or "visibility" => "View",
        "copy" or "clipboard" or "files" => "Copy",
        "filter" or "funnel" => "Filter",
        "refresh" or "arrow-clockwise" or "arrow-repeat" or "sync" => "Refresh",
        "list" or "menu" or "bars" or "hamburger" => "Menu",
        "share" => "Share",
        "save" or "floppy" => "Save",
        "three-dots" or "three-dots-vertical" or "ellipsis" or "more" or "more-vert" or "dots" => "More",
        "chevron-left" or "arrow-left" or "caret-left" => "Back",
        "chevron-right" or "arrow-right" or "caret-right" => "Next",
        "caret-down" or "caret-down-fill" or "chevron-down" or "arrow-down" => "Expand",
        "caret-up" or "caret-up-fill" or "chevron-up" or "arrow-up" => "Collapse",
        "calendar" or "calendar-event" or "event" => "Calendar",
        var other => CSharp.Pascal(other) is { } pascal ? StepNamer.Human(pascal) is var words && words.Length > 0
            ? char.ToUpperInvariant(words[0]) + words[1..]
            : null : null,
    };

    /// <summary>
    /// A value someone might type rather than what the field is called: an address, a number, a date or its format, an
    /// example ("e.g. ...").
    /// </summary>
    public static bool IsExample(string text) =>
        text.Contains('@') || ExampleValue().IsMatch(text) || text.Count(char.IsDigit) >= 3;

    private static bool InColumnHeader(CapturedTarget field) =>
        field.Candidates.Any(candidate => candidate.Parts.Any(part => part is { M: "role", Role: "columnheader" })
                                          || candidate.Csharp?.StartsWith("GetByRole(AriaRole.Columnheader", StringComparison.Ordinal) == true);

    // What a prompt says the field is: "Select State" gives State, "Type comment here" Comment; none of an example.
    private static string? Prompt(string? text)
    {
        if (Clean(text) is not { } said || IsExample(said)) return null;
        var words = AsciiWord().Matches(said).Select(word => word.Value).Where(word => !PromptWords.Contains(word)).ToList();
        return words.Count is > 0 and <= 4 ? CSharp.Pascal(string.Join(' ', words)) : null;
    }

    // The label says what the id says in fewer words, which a person reads: userEmail is the Email.
    private static bool Within(string label, string name)
    {
        var words = StepNamer.Words(name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var said = StepNamer.Words(label).ToList();
        return said.Count > 0 && said.Count < words.Count && said.All(words.Contains);
    }

    private static string WithoutKindWords(string pascal)
    {
        var words = StepNamer.Words(pascal).ToList();
        while (words.Count > 1 && KindWords.Contains(words[^1])) words.RemoveAt(words.Count - 1);
        return words.Count == StepNamer.Words(pascal).Count() ? pascal : string.Concat(words);
    }

    // A label in English, as a setting's name.
    private static string? English(string? text) =>
        Clean(text) is { } clean && CSharp.IsAsciiText(clean) && !IsExample(clean) ? CSharp.Pascal(clean) : null;

    // A label without its hint in brackets or after a colon: "Mobile(10 Digits)" gives Mobile.
    private static string? Hintless(string? label) =>
        label is null ? null : Hint().Replace(label, "").Trim(' ', ':', '*');

    private static string? Clean(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim().TrimEnd('*', ':').Trim() is { Length: > 0 } clean ? clean : null;

    [GeneratedRegex(@"[-_]\d+$")]
    private static partial Regex NumberedId();

    [GeneratedRegex(@"\s*[(\[][^)\]]*[)\]]")]
    private static partial Regex Hint();

    [GeneratedRegex(@"^(e\.?g\.?|ex\.?|example)\b|https?://|www\.|\b(yyyy|yy|mm|dd|hh)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ExampleValue();

    [GeneratedRegex("[A-Za-z0-9]+")]
    private static partial Regex AsciiWord();
}
