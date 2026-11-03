using QaaS.Playwright.Recorder.Capture;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>Short ways to write what the capture script reports, for tests of what the recorder makes of it.</summary>
internal static class Captured
{
    private static int _next;

    public static CapturedCandidate Css(string kind, string css, string? value = null, int count = 1, string? dialog = null) =>
        Candidate(kind, count, [.. Scope(dialog), new LocatorPart { M = "css", Css = css }], value);

    public static CapturedCandidate Role(string role, string name, bool exact = true, int count = 1, string? dialog = null) =>
        Candidate("role", count, [.. Scope(dialog), new LocatorPart { M = "role", Role = role, Name = name, Exact = exact }],
            text: name);

    public static CapturedCandidate RoleRegex(string role, string regex, string text, string? dialog = null) =>
        Candidate("role", 1, [.. Scope(dialog), new LocatorPart { M = "role", Role = role, Regex = regex }], text: text);

    public static CapturedCandidate Generated(string csharp) =>
        new() { Kind = "generator", Csharp = csharp, Count = 1, Matches = true };

    /// <summary>An element, with the name and id attributes its candidates are built on.</summary>
    public static CapturedTarget Element(string role, string text, params CapturedCandidate[] candidates) => new()
    {
        Tag = role, Role = role, Text = text, Interactive = true, Candidates = [.. candidates],
        Name = candidates.FirstOrDefault(candidate => candidate.Kind == "name")?.Value,
        Id = candidates.FirstOrDefault(candidate => candidate.Kind == "id")?.Value,
    };

    public static CapturedDialog Dialog(string title) =>
        new() { Title = title, Raw = title, TitleMatches = true, UnnamedMatches = true };

    public static CapturedAction Action(string kind, CapturedTarget? target, string? value = null, string? dialog = null,
        string? widget = null, params CapturedEffect[] effects) => new()
    {
        Id = $"a-{++_next}", T = 1_000_000 + _next * 1_000, Kind = kind, Target = target, Value = value, Widget = widget,
        Dialog = dialog is null ? null : Dialog(dialog), OpenDialogs = dialog is null ? [] : [dialog],
        Url = "https://app.test/start", Effects = [.. effects],
    };

    public static CapturedEffect Opened(string title) => new() { Kind = "dialogOpened", Dialog = Dialog(title) };

    public static CapturedEffect Closed(string title) => new() { Kind = "dialogClosed", Dialog = Dialog(title) };

    public static CapturedEffect Alert(string text, int count = 1) => new() { Kind = "alert", Text = text, AlertCount = count };

    public static CapturedEffect UrlChanged(string url) => new() { Kind = "urlChanged", Url = url, Page = "page" };

    /// <summary>A busy indicator came up and went away: <c>progressbar</c> or <c>ariaBusy</c>.</summary>
    public static CapturedEffect Busy(string kind = "progressbar") => new() { Kind = "busy", Value = kind };

    public static Recording Recording(params CapturedAction[] actions) => new("https://app.test/start", actions);

    private static CapturedCandidate Candidate(string kind, int count, List<LocatorPart> parts, string? value = null,
        string? text = null) =>
        new() { Kind = kind, Parts = parts, Count = count, Matches = count == 1, Value = value, Text = text };

    private static IEnumerable<LocatorPart> Scope(string? dialog) =>
        dialog is null ? [] : [new LocatorPart { M = "role", Role = "dialog", Name = dialog }];
}
