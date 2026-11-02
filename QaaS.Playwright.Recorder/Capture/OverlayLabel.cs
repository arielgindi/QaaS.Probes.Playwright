using System.Text.Json;
using QaaS.Playwright.Recorder.Generation;

namespace QaaS.Playwright.Recorder.Capture;

/// <summary>
/// What the recording overlay shows for the element under the pointer, answered for the capture script through the
/// binding <c>__qaasExplain</c>: the locator a click there records, picked by the same policy the flow is written
/// with, and why each locator tried before it lost.
/// </summary>
internal static class OverlayLabel
{
    /// <summary>
    /// The label, as JSON, for the capture script's description of the element and of its equivalents, as JSON. Its
    /// <c>element</c> tells which one the click is recorded on: 0 for the element, n for its n-th equivalent.
    /// </summary>
    /// <exception cref="JsonException">The text is not a description.</exception>
    public static string For(string json)
    {
        var question = JsonSerializer.Deserialize<Question>(json, Recording.Json) ?? new Question();
        var target = LocatorPolicy.Prefer(question.Target, question.Equivalents);
        var chosen = LocatorPolicy.Choose(target, question.ByName);
        var answer = new Answer(
            target == question.Target ? 0 : question.Equivalents!.IndexOf(target!) + 1,
            LocatorPolicy.Describe(chosen),
            LocatorPolicy.Problem(chosen),
            target is null ? [] : LocatorPolicy.WhyNot(target, chosen, question.ByName),
            target is { Interactive: false } ? "not a control: kept only if the page reacts" : null);
        return JsonSerializer.Serialize(answer, Recording.Json);
    }

    private sealed class Question
    {
        public CapturedTarget? Target { get; set; }

        /// <summary>Elements the same click hits, which the click may be located by instead.</summary>
        public List<CapturedTarget>? Equivalents { get; set; }

        /// <summary>A radio, located by its option's name first.</summary>
        public bool ByName { get; set; }
    }

    private sealed record Answer(int Element, string? Locator, string? Problem, IReadOnlyList<string> Reasons, string? Note);
}
