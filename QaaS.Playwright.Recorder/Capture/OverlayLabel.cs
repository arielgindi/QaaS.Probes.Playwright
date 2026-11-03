using System.Text.Json;
using QaaS.Playwright.Recorder.Generation;

namespace QaaS.Playwright.Recorder.Capture;

/// <summary>
/// What the recording overlay shows for the element under the pointer, answered for the capture script through the
/// binding <c>__qaasExplain</c>: the locator a click there records, picked by the same policy the flow is written
/// with, how many elements it found, and why each locator tried before it lost.
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
            chosen?.Count,
            LocatorPolicy.Problem(chosen),
            target is null ? [] : LocatorPolicy.WhyNot(target, chosen, question.ByName),
            target is { Interactive: false } ? "not a control: kept only if the page reacts" : null);
        return JsonSerializer.Serialize(answer, Recording.Json);
    }

    /// <summary>
    /// The capture script's call that shows the label: <see cref="For"/> the question, with the number the script asked
    /// it under, as it gets no answer from the binding itself.
    /// </summary>
    public static object Reply(string json) => new
    {
        op = "explained",
        seq = JsonSerializer.Deserialize<Question>(json, Recording.Json)?.Seq ?? 0,
        label = For(json),
    };

    private sealed class Question
    {
        public long Seq { get; set; }

        public CapturedTarget? Target { get; set; }

        /// <summary>Elements the same click hits, which the click may be located by instead.</summary>
        public List<CapturedTarget>? Equivalents { get; set; }

        /// <summary>A radio, located by its option's name first.</summary>
        public bool ByName { get; set; }
    }

    /// <summary>The element it is recorded on, the locator and how many elements it found, and what to say of it.</summary>
    private sealed record Answer(int Element, string? Locator, int? Count, string? Problem, IReadOnlyList<string> Reasons, string? Note);
}
