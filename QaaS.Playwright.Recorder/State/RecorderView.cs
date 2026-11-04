namespace QaaS.Playwright.Recorder.State;

/// <summary>
/// How a mode is told to the person, the same in the console's mode line, the panel's status strip and the page's pill:
/// <paramref name="Name"/> (the strip's accessible name), its colour, its word, what it means, and the key that leaves it.
/// </summary>
internal sealed record ModeView(string Name, string Tone, string Label, string Message, string? Key);

/// <summary>
/// The one place that says, from a <see cref="RecorderState"/>, what mode the person is in, most pressing first. The
/// console, the panel and each page render it; none of them decides it.
/// </summary>
internal static class RecorderView
{
    public static ModeView Of(RecorderState s) => s switch
    {
        { Mode: Mode.Saving or Mode.Ended } => new("Saving", "accent", "Saving", "the flow and its settings…", null),
        { Mode: Mode.Starting } => new("Starting", "accent", "Starting", "Chrome opens…", null),
        { Mode: Mode.LoggingIn } => new("Logging in", "info", "Logging in", "Nothing the login flow does is recorded", "Esc"),
        { Mode: Mode.Asking } => new("Replaying", "bad", "Replay stopped", "In the console: keep recording from here, or stop", "Enter"),
        { Part.Phase: PartPhase.Replaying } p => new("Re-recording", "rec", p.Part!.After ? "Record after" : "Re-record",
            $"Replaying the flow up to {(p.Part.After ? "the end of " : "")}{p.Part.Label}… nothing is recorded", "Esc"),
        { Part.Phase: PartPhase.Failed } p => new("Re-recording", "bad", "Replay failed", $"It did not get to {p.Part!.Label}", "Esc"),
        { Run.Kind: RunKind.ContinueReplay } => new("Replaying", "info", "Replaying", "The flow continued replays here: nothing is recorded", "Esc"),
        { Mode: Mode.Testing } => new("Testing", "accent", "Testing", "Nothing is recorded while the flow runs", "Esc"),
        { Check: not null } => new("Check mode", "ok", "Check", "Click what the flow should check in the app, then choose the check", "Esc"),
        { Inspect: not null } => new("Inspect", "inspect", "Inspect", "Point at an element in the app; a click there picks it", "Esc"),
        { Mode: Mode.Trying } => new("Trying", "try", "Trying", "Nothing is recorded: each action shows as it would be written", "F7"),
        { Mode: Mode.Paused, Notice: { } notice } => new("Paused", "warn", "Paused", $"{notice} Log in by hand in the app, then resume.", "F8"),
        { Mode: Mode.Paused, Part.Phase: PartPhase.Recording } p => new("Paused", "warn", "Paused", $"Nothing you do is recorded; F8 resumes {p.Part!.Label}", "F8"),
        { Mode: Mode.Paused } => new("Paused", "warn", "Paused", "Nothing you do is recorded; F8 resumes", "F8"),
        { Part.Phase: PartPhase.Recording } p => new("Re-recording", "rec", p.Part!.After ? "Recording after" : "Re-recording",
            $"{p.Part.Label}: {(p.Part.After ? "record what goes there" : "do it again")}, then press Done", "F2"),
        { Part.Phase: PartPhase.Done } p => new("Re-recording", "ok", "Re-recorded", $"Re-recorded {p.Part!.Label}. Test it to see it run as it will be saved.", null),
        _ => new("Recording", "rec", "Recording", "Each action you do in the app is added to the flow", null),
    };

    /// <summary>
    /// The pill at the top of <paramref name="page"/>: its mode, check and inspect mode on their own page only; null
    /// while it only records, as nothing then needs saying.
    /// </summary>
    public static ModeView? PillOf(RecorderState s, string page)
    {
        var here = s with
        {
            Check = s.Check?.Page == page ? s.Check : null,
            Inspect = s.Inspect?.Page == page ? s.Inspect : null,
        };
        var view = Of(here);
        return view.Name == "Recording" || view is { Name: "Re-recording", Label: "Re-recorded" } ? null : view;
    }
}
