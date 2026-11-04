using System.Text.RegularExpressions;

namespace QaaS.Playwright.Recorder.Capture;

/// <summary>
/// A recording as the generator reads it, made whole by <see cref="RecordingReader"/>: every list and text there (empty
/// where it was left out or null), every id unique, every action, effect and locator part of a kind the recorder knows,
/// every page named as the recorder names them, and every secret a <see cref="Secret"/>, out of every text. What could
/// not be made whole is in <see cref="Unrecorded"/>, said for the person to do by hand.
/// </summary>
internal sealed class ValidRecording
{
    private ValidRecording(Recording recording, IReadOnlyList<string> unrecorded) =>
        (Recording, Unrecorded) = (recording, unrecorded);

    /// <summary>The recording with the actions the generator writes.</summary>
    public Recording Recording { get; }

    public IReadOnlyList<CapturedAction> Actions => Recording.Actions;

    /// <summary>What was left out of <see cref="Recording"/>, and why, to check by hand.</summary>
    public IReadOnlyList<string> Unrecorded { get; }

    internal static ValidRecording Of(Recording recording, IReadOnlyList<string> unrecorded) => new(recording, unrecorded);
}

/// <summary>
/// The one way a recording reaches the generator, from a file (autosave, <c>--continue</c>, <c>--keep-recording</c>), the
/// capture or the panel's edits. It repairs what another version of the recorder, a hand or a bug left: a list or a text
/// null as empty, an item null left out, an id given twice made unique, a page of no name the page, one named otherwise
/// a <c>pageN</c> of its own. A tab or pop-up of the page itself is a navigation, or nothing. An action of a kind it
/// does not know is left out and said; an effect or a locator of one, left out. A secret typed or checked moves into
/// <see cref="CapturedAction.Secret"/>, out of every text the recording serialises. Reading again reads the same: the
/// actions are repaired in place, so a recording read twice, or its actions read in another, stay as read.
/// </summary>
internal static partial class RecordingReader
{
    private static readonly HashSet<string> Kinds =
        ["click", "dblclick", "fill", "check", "uncheck", "choose", "press", "upload", "drag", "navigate", "newTab", "expect"];

    private static readonly HashSet<string> EffectKinds =
        ["dialogOpened", "dialogClosed", "alert", "tabSelected", "settled", "changed", "busy", "urlChanged", "popup", "jsDialog"];

    private static readonly HashSet<string> PartKinds = ["css", "testid", "text", "title", "filter", "has", "role"];

    /// <exception cref="System.Text.Json.JsonException">The text is not a recording.</exception>
    public static ValidRecording FromJson(string json) => Read(Recording.FromJson(json));

    public static ValidRecording Read(Recording recording)
    {
        var unrecorded = new List<string>();
        var pages = Pages(recording.Actions);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<CapturedAction>();
        foreach (var (action, index) in recording.Actions.Select((action, index) => (action, index)))
        {
            Repair(action, pages);
            action.Id = Unique(action.Id is { Length: > 0 } id ? id : $"action-{index + 1}", ids);
            if (Kinds.Contains(action.Kind))
                kept.Add(action);
            else
                unrecorded.Add($"action {index + 1}, a '{action.Kind}' the recorder does not know, was left out: redo it by hand");
        }

        return ValidRecording.Of(kept.Count == recording.Actions.Count ? recording : recording with { Actions = kept }, unrecorded);
    }

    // A second action of an id seen, as one added twice, is told apart by a number.
    private static string Unique(string id, HashSet<string> taken)
    {
        var unique = id;
        for (var copy = 2; !taken.Add(unique); copy++) unique = $"{id}~{copy}";
        return unique;
    }

    // Each page's name as the flow names its variable: page, or pageN; a name of another form gets a pageN of its own,
    // the same wherever it is named.
    private static Func<string?, string> Pages(IReadOnlyList<CapturedAction> actions)
    {
        var named = new Dictionary<string, string>(StringComparer.Ordinal);
        var taken = actions.Select(action => action.Page).Concat(actions.SelectMany(action => action.Effects ?? []).Select(effect => effect?.Page))
            .OfType<string>().Where(page => PageName().IsMatch(page)).ToHashSet(StringComparer.Ordinal);
        return page =>
        {
            if (string.IsNullOrWhiteSpace(page)) return "page";
            if (PageName().IsMatch(page)) return page;
            if (named.TryGetValue(page, out var name)) return name;
            var number = 1;
            while (!taken.Add($"page{number}")) number++;
            return named[page] = $"page{number}";
        };
    }

    private static void Repair(CapturedAction action, Func<string?, string> page)
    {
        action.Kind ??= "";
        action.Page = page(action.Page);
        action.Effects ??= [];
        action.Effects.RemoveAll(effect => effect is null);
        foreach (var effect in action.Effects)
        {
            effect.Kind ??= "";
            if (effect.Page is not null) effect.Page = page(effect.Page);
            Dialog(effect.Dialog);
        }

        // An effect of a kind no version knows, an address change of no address, a pop-up of no page or of the page itself.
        action.Effects.RemoveAll(effect => !EffectKinds.Contains(effect.Kind) || effect is { Kind: "urlChanged", Url: null or "" }
                                           || effect is { Kind: "popup" } && (effect.Page is null or "page" || effect.Page == action.Page));
        // The page the recording started on is no tab opened.
        if (action is { Kind: "newTab", Page: "page" }) action.Kind = "navigate";

        action.OpenDialogs?.RemoveAll(title => title is null);
        action.Files?.RemoveAll(file => file is null);
        action.Equivalents?.RemoveAll(target => target is null);
        Dialog(action.Dialog);
        foreach (var target in new[] { action.Target, action.Field, action.Option, action.To, action.HoverOpener, action.Group, action.Scrolled?.List }
                     .Concat(action.Equivalents ?? []))
            Target(target);
        action.Edit?.Dropped?.RemoveAll(key => key is null);
        if (action.Edit?.Values is { } values)
            foreach (var role in values.Where(value => value.Value is null).Select(value => value.Key).ToList())
                values.Remove(role);
        Hide(action);
    }

    // A secret typed, or checked, or given as the default of its setting, is a Secret, out of the texts serialised.
    private static void Hide(CapturedAction action)
    {
        if (!SecretField.Holds(action)) return;
        var given = action.Edit?.Values?.GetValueOrDefault("value");
        var value = given?.Value ?? action.Raw ?? action.Value;
        if (value is not null) action.Secret = Secret.Of(value);
        (action.Value, action.Raw) = (null, null);
        if (given is not null) given.Value = null;
    }

    private static void Dialog(CapturedDialog? dialog)
    {
        if (dialog is not null) dialog.Title ??= "";
    }

    private static void Target(CapturedTarget? target)
    {
        if (target is null) return;
        target.Tag ??= "";
        target.Candidates ??= [];
        target.Candidates.RemoveAll(candidate => candidate is null);
        foreach (var candidate in target.Candidates)
        {
            candidate.Kind ??= "";
            candidate.Parts ??= [];
            candidate.Parts.RemoveAll(part => part is null);
            foreach (var part in candidate.Parts) part.M ??= "";
        }

        // A locator of a part no version knows is none: what it would find is not known.
        target.Candidates.RemoveAll(candidate => candidate.Parts.Any(part => !PartKinds.Contains(part.M)));

        // A row is told by its key, and a row of no key is none.
        if (target.Row is { Key: null or "" }) target.Row = null;
    }

    // As the recorder names a page: page, and page1, page2, ... for the pop-ups and tabs it opened.
    [GeneratedRegex(@"^page\d*$")]
    private static partial Regex PageName();
}
