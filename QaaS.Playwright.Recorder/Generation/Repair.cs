using System.Text.RegularExpressions;
using QaaS.Playwright.Recorder.Capture;

namespace QaaS.Playwright.Recorder.Generation;

/// <summary>
/// Makes what a recording leaves out, or holds as null, what the generator reads as missing: a list as empty, a text as
/// empty, an action on no page as one on the page. A recording read from a file, written by hand or by another version of
/// the recorder, may hold <c>"effects": null</c>, an item null or a page of no name, where the recorder writes none.
/// A page not named as the recorder names them (<c>page</c>, <c>page1</c>, ...) gets a name of its own that is, the
/// same wherever it is named, as the flow declares a variable of each.
/// </summary>
internal static partial class Repair
{
    public static void Recording(Recording recording)
    {
        var pages = new Dictionary<string, string>(StringComparer.Ordinal);
        var taken = recording.Actions.Select(action => action.Page)
            .Concat(recording.Actions.SelectMany(action => action.Effects ?? []).Select(effect => effect?.Page))
            .OfType<string>().Where(page => PageName().IsMatch(page)).ToHashSet(StringComparer.Ordinal);
        string Page(string? page)
        {
            if (string.IsNullOrWhiteSpace(page)) return "page";
            if (PageName().IsMatch(page)) return page;
            if (pages.TryGetValue(page, out var named)) return named;
            var number = 1;
            while (!taken.Add($"page{number}")) number++;
            return pages[page] = $"page{number}";
        }

        foreach (var action in recording.Actions)
        {
            action.Id ??= "";
            action.Kind ??= "";
            action.Page = Page(action.Page);
            action.Effects ??= [];
            action.Effects.RemoveAll(effect => effect is null);
            foreach (var effect in action.Effects)
            {
                effect.Kind ??= "";
                if (effect.Page is not null) effect.Page = Page(effect.Page);
                Dialog(effect.Dialog);
            }

            // The page the recording started on is no tab opened, nor a pop-up of another page; nor is a page its own.
            if (action is { Kind: "newTab", Page: "page" }) action.Kind = "navigate";
            action.Effects.RemoveAll(effect => effect is { Kind: "popup" } && (effect.Page == "page" || effect.Page == action.Page));

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
        }
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
    }

    // As the recorder names a page: page, and page1, page2, ... for the pop-ups and tabs it opened.
    [GeneratedRegex(@"^page\d*$")]
    private static partial Regex PageName();
}
