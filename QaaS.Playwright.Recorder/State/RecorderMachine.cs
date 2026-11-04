namespace QaaS.Playwright.Recorder.State;

/// <summary>What the recorder is doing: the one main mode of a <see cref="RecorderState"/>.</summary>
internal enum Mode
{
    /// <summary>The recorder's window asks what to record, before Chrome opens (the setup screen, to come).</summary>
    Setup,

    /// <summary>Chrome is opening; nothing is recorded yet.</summary>
    Starting,

    /// <summary>The login flow runs (<see cref="RunKind.Login"/>): nothing it does is recorded.</summary>
    LoggingIn,

    Recording,

    /// <summary>Nothing is recorded until the person resumes.</summary>
    Paused,

    /// <summary>Nothing is recorded; what the person does is shown as the recorder would write it.</summary>
    Trying,

    /// <summary>A run of the flow (<see cref="RecorderState.Run"/>): a test, or a replay in the recorded window.</summary>
    Testing,

    /// <summary>The steps of a flow continued did not all replay: the console asks whether to keep recording.</summary>
    Asking,

    /// <summary>The recording ends: it is drained, kept and saved; nothing more is recorded.</summary>
    Saving,

    /// <summary>Saved, the window stays open on what was saved (the setup screen's, to come).</summary>
    Done,

    Ended,
}

/// <summary>What a run of user code in a <see cref="RunScope"/> is.</summary>
internal enum RunKind
{
    Login,
    Test,

    /// <summary>The flow replayed from its start up to a part recorded again, in the recorded window.</summary>
    PartReplay,

    /// <summary>The steps of the recording continued, replayed in the recorded window.</summary>
    ContinueReplay,
}

/// <summary>How a run ended.</summary>
internal enum RunOutcome { Passed, Failed, Stopped }

/// <summary>Where a part recorded again is.</summary>
internal enum PartPhase
{
    /// <summary>The flow replays up to it.</summary>
    Replaying,

    /// <summary>The replay did not get to it; the person records again from where it failed, or cancels.</summary>
    Failed,

    /// <summary>What the person does goes in its place.</summary>
    Recording,

    /// <summary>Kept, until dismissed or undone.</summary>
    Done,
}

/// <summary>A page's document, which check and inspect mode belong to: a reload or a new address is another document.</summary>
internal sealed record PageDocument(string Page, string Document);

/// <summary>A run of user code going on: its kind and its number, so that the end of an older one is told apart.</summary>
internal sealed record RunInfo(RunKind Kind, long Id);

/// <summary>
/// A part of the flow recorded again (or recorded after an action): <paramref name="Label"/> as the panel names it, whether
/// what is recorded goes <paramref name="After"/> it, where it is, whether it is the end of the flow
/// (<paramref name="AtEnd"/>: done, the recording goes on there), and what the recording did when it began
/// (<paramref name="WasRecording"/>: recording or trying, rather than paused), to go back to when it is cancelled.
/// </summary>
internal sealed record PartState(string Label, bool After, PartPhase Phase, bool AtEnd, bool WasRecording);

/// <summary>
/// The recorder's state, as one immutable snapshot: the main <see cref="Mode"/>, what Testing and Trying go back to
/// (<see cref="Then"/>), the run going on, and the regions that hold beside the mode: a part recorded again, and the
/// page in check or inspect mode. <see cref="Epoch"/> numbers the snapshots; it is the capture pipeline's epoch, which
/// every message of a page carries, so a message begun in a recording epoch is kept and one begun in another is not.
/// </summary>
internal sealed record RecorderState
{
    public long Epoch { get; init; }
    public Mode Mode { get; init; } = Mode.Starting;

    /// <summary>For Testing and Trying: what the recording goes back to, Recording or Paused (or Trying, after a test).</summary>
    public Mode Then { get; init; } = Mode.Recording;

    public RunInfo? Run { get; init; }
    public PartState? Part { get; init; }
    public PageDocument? Check { get; init; }
    public PageDocument? Inspect { get; init; }

    /// <summary>Why the recording is paused when the person did not pause it, e.g. the login flow failed.</summary>
    public string? Notice { get; init; }

    public StopReason? Stop { get; init; }

    /// <summary>
    /// Whether what the person does in the app is recorded now: while recording, outside check and inspect mode. The
    /// capture pipeline keeps a page's message only when it began in an epoch that records.
    /// </summary>
    public bool Records => Mode == Mode.Recording && Check is null && Inspect is null;

    /// <summary>Whether the recording's log takes what the pages tell (it does in check and inspect mode, for checks).</summary>
    public bool LogOpen => Mode == Mode.Recording;

    /// <summary>The start of every recording: Chrome opening.</summary>
    public static RecorderState Initial { get; } = new();
}

/// <summary>What the person or the recorder asks the state machine for.</summary>
internal abstract record Command
{
    /// <summary>Whether the person asks it (a key, the panel, the console), rather than the recorder telling what happened.</summary>
    public virtual bool ByThePerson => true;
}

/// <summary>Chrome is open: the login flow runs first, or the recording continued replays, or it records.</summary>
internal sealed record Started(bool LogsIn, bool Continues) : Command
{
    public override bool ByThePerson => false;
}

/// <summary>F8, or Pause and Resume in the panel.</summary>
internal sealed record TogglePause : Command;

/// <summary>The recording pauses for a reason of the recorder's (a login that failed), saying it.</summary>
internal sealed record PauseWithNotice(string Notice) : Command
{
    public override bool ByThePerson => false;
}

/// <summary>F7, or Try and Stop trying in the panel.</summary>
internal sealed record ToggleTry : Command;

/// <summary>F6, or Check in the panel, on the page in front, as its document is now.</summary>
internal sealed record CheckOn(PageDocument Page) : Command;

/// <summary>Esc or F6 in check mode, or Leave in the panel; or the page said it left check mode.</summary>
internal sealed record CheckOff : Command;

/// <summary>Ctrl+Shift+C, in the app or the panel.</summary>
internal sealed record InspectOn(PageDocument Page) : Command;

internal sealed record InspectOff : Command;

/// <summary>A page's document changed: it loaded anew, or went to another address. Its check and inspect mode end.</summary>
internal sealed record DocumentChanged(PageDocument Page) : Command
{
    public override bool ByThePerson => false;
}

/// <summary>Test, Ctrl+Enter or F5 in the panel; Run to here.</summary>
internal sealed record Test : Command;

/// <summary>Stop, Esc or Shift+F5 in the panel: the run going on ends at once.</summary>
internal sealed record StopRun : Command;

/// <summary>A run of user code ended by itself (<paramref name="Id"/> is its <see cref="RunInfo.Id"/>).</summary>
internal sealed record RunEnded(long Id, RunOutcome Outcome) : Command
{
    public override bool ByThePerson => false;
}

/// <summary>Re-record, or Record after this, in the panel: the flow replays from its start up to the part.</summary>
internal sealed record Rerecord(string Label, bool After, bool AtEnd) : Command;

/// <summary>Re-record from step N, in the panel, once the replay did not get to the part.</summary>
internal sealed record RerecordFromFailure : Command;

/// <summary>F2, or Done in the panel: what was recorded in the part stays.</summary>
internal sealed record PartDone : Command;

/// <summary>Keep it as it was, Esc in the panel, or Undo of the part: the part stays as it was.</summary>
internal sealed record PartCancel : Command;

/// <summary>The app went back, or not, to where it was when the part began, after it was cancelled.</summary>
internal sealed record ReturnedToStart(bool Back) : Command
{
    public override bool ByThePerson => false;
}

/// <summary>The panel's ✕ on a part done.</summary>
internal sealed record PartDismiss : Command;

/// <summary>Esc while the login flow runs, or Log in by hand in the panel; <paramref name="Why"/> it stopped, as the pause says.</summary>
internal sealed record LogInByHand(string? Why = null) : Command;

/// <summary>
/// The login flow ended by itself: <paramref name="Failure"/> null when it logged in, else why not; then the steps of a
/// flow it <paramref name="Continues"/> replay.
/// </summary>
internal sealed record LoginEnded(string? Failure, bool Continues = false) : Command
{
    public override bool ByThePerson => false;
}

/// <summary>The person's answer to the console's question once the steps of a flow continued did not all replay.</summary>
internal sealed record KeepRecording(bool Keep) : Command;

/// <summary>
/// The <see cref="RecorderLifetime"/> ends the recording, for its <see cref="StopReason"/>: nothing more is recorded. The
/// run going on stays the state's until the lifetime has saved and disposed the scopes, in its order.
/// </summary>
internal sealed record Stop(StopReason Reason) : Command;

/// <summary>The recording was saved, or kept, and every scope disposed.</summary>
internal sealed record Ended : Command
{
    public override bool ByThePerson => false;
}

/// <summary>What a transition does beside changing the state, in this order, after the pages were drained.</summary>
internal enum Effect
{
    /// <summary>The recording's log takes nothing more.</summary>
    PauseLog,

    /// <summary>The log takes what the pages tell again; a page that went elsewhere meanwhile is gone to by its address.</summary>
    ResumeLog,

    /// <summary>The log takes what the pages tell again, from where the replay left them.</summary>
    ResumeLogRebased,

    /// <summary>What the person tries is collected apart, from now.</summary>
    StartTrying,

    /// <summary>What was tried is dropped, and its cards with it.</summary>
    StopTrying,

    /// <summary>A run of user code starts in a new scope (the state's <see cref="RecorderState.Run"/>).</summary>
    StartRun,

    /// <summary>The scope of the run going on is disposed: whatever it waits for fails at once, and its pages close.</summary>
    DisposeRun,

    /// <summary>What is recorded from now goes in place of the part.</summary>
    OpenPart,

    /// <summary>The part ends, kept.</summary>
    KeepPart,

    /// <summary>The part ends as it was.</summary>
    DropPart,

    /// <summary>The app goes back to the page it was on when the part began; <see cref="ReturnedToStart"/> says how it went.</summary>
    GoBack,

    /// <summary>The login's page is replaced by a fresh one at the same address, the login so far kept.</summary>
    ReplaceLoginPage,

    /// <summary>The steps of the flow continued that did not replay are left out.</summary>
    LeaveOutNotReplayed,
}

/// <summary>What a request came to: accepted, with the state it led to and its effects, or refused, and why.</summary>
internal sealed record Outcome(bool Accepted, RecorderState State, IReadOnlyList<Effect> Effects, string? Refused)
{
    public static Outcome Refuse(RecorderState state, string why) => new(false, state, [], why);
}

/// <summary>
/// The table of the recorder's states: for each command, in which states it is allowed, what it leads to, and what it
/// does beside. A command no row allows is refused, with why, and changes nothing. It is pure: the
/// <see cref="RecorderStateMachine"/> applies it, after draining the pages, and carries out the effects.
/// </summary>
internal static class RecorderTable
{
    /// <summary>What <paramref name="command"/> leads to from <paramref name="state"/>: the next state and its effects, or why not.</summary>
    public static Outcome Apply(RecorderState state, Command command)
    {
        var (next, effects, refused) = Decide(state, command);
        return refused is not null || next is null
            ? Outcome.Refuse(state, refused ?? "Not now.")
            : new Outcome(true, next with { Epoch = state.Epoch + 1 }, effects, null);
    }

    /// <summary>The commands of the person allowed in <paramref name="state"/>, for the panel's buttons and keys and the page's.</summary>
    public static IReadOnlySet<string> Allowed(RecorderState state) =>
        Examples(state).Where(command => command.ByThePerson && Apply(state, command).Accepted)
            .Select(command => command.GetType().Name).ToHashSet();

    /// <summary>One command of each kind, as the state could be sent them, for <see cref="Allowed"/> and the table's tests.</summary>
    public static IEnumerable<Command> Examples(RecorderState state)
    {
        var page = state.Check ?? state.Inspect ?? new PageDocument("page", "d1");
        return
        [
            new Started(LogsIn: false, Continues: false), new Started(LogsIn: true, Continues: false), new Started(LogsIn: false, Continues: true),
            new TogglePause(), new PauseWithNotice("The login flow failed."), new ToggleTry(), new CheckOn(page), new CheckOff(),
            new InspectOn(page), new InspectOff(), new DocumentChanged(page with { Document = page.Document + "'" }),
            new Test(), new StopRun(), new RunEnded(state.Run?.Id ?? 0, RunOutcome.Passed),
            new RunEnded(state.Run?.Id ?? 0, RunOutcome.Failed), new RunEnded(state.Run?.Id ?? 0, RunOutcome.Stopped),
            new Rerecord("FillAsync", After: false, AtEnd: false), new Rerecord("SaveAsync", After: true, AtEnd: true),
            new RerecordFromFailure(), new PartDone(), new PartCancel(), new ReturnedToStart(true), new ReturnedToStart(false),
            new PartDismiss(), new LogInByHand(), new LoginEnded(null), new LoginEnded(null, Continues: true), new LoginEnded("It waited 30 s."),
            new KeepRecording(true), new KeepRecording(false), new Stop(StopReason.Saved), new Stop(StopReason.CtrlC), new Ended(),
        ];
    }

    private static (RecorderState? Next, Effect[] Effects, string? Refused) Decide(RecorderState s, Command command)
    {
        if (s.Mode == Mode.Ended) return Refuse("The recording ended.");
        switch (command)
        {
            case Started started when s.Mode == Mode.Starting:
                return started.LogsIn ? Go(s with { Mode = Mode.LoggingIn, Run = NewRun(s, RunKind.Login) }, Effect.StartRun)
                    : started.Continues ? Go(s with { Mode = Mode.Testing, Run = NewRun(s, RunKind.ContinueReplay), Then = Mode.Recording }, Effect.StartRun)
                    : Go(s with { Mode = Mode.Recording }, Effect.ResumeLog);
            case Started:
                return Refuse("The recording started already.");

            case LoginEnded ended when s.Mode == Mode.LoggingIn:
                return ended.Failure is { } why ? Go(s with { Mode = Mode.Paused, Then = Mode.Paused, Run = null, Notice = why }, Effect.DisposeRun)
                    : ended.Continues ? Go(s with { Mode = Mode.Testing, Then = Mode.Recording, Run = NewRun(s, RunKind.ContinueReplay) }, Effect.DisposeRun, Effect.StartRun)
                    : Go(s with { Mode = Mode.Recording, Run = null, Notice = null }, Effect.DisposeRun, Effect.ResumeLog);
            case LoginEnded:
                return Refuse("No login flow runs.");
            case LogInByHand byHand when s.Mode == Mode.LoggingIn:
                return Go(s with { Mode = Mode.Paused, Then = Mode.Paused, Run = null, Notice = byHand.Why ?? "You stopped the login flow." },
                    Effect.DisposeRun, Effect.ReplaceLoginPage);
            case LogInByHand:
                return Refuse("No login flow runs.");

            case TogglePause when s.Mode == Mode.Recording:
                return Go(s with { Mode = Mode.Paused, Then = Mode.Paused, Notice = null }, Effect.PauseLog);
            case TogglePause when s.Mode == Mode.Paused:
                return Go(s with { Mode = Mode.Recording, Notice = null }, Effect.ResumeLog);
            case TogglePause:
                return Refuse(Why(s, "pause or resume"));
            case PauseWithNotice paused when s.Mode is Mode.Recording or Mode.Paused:
                return Go(s with { Mode = Mode.Paused, Then = Mode.Paused, Notice = paused.Notice, Check = null, Inspect = null },
                    s.Mode == Mode.Recording ? [Effect.PauseLog] : []);
            case PauseWithNotice:
                return Refuse(Why(s, "pause"));

            case ToggleTry when s.Mode is Mode.Recording or Mode.Paused:
                return Go(s with { Mode = Mode.Trying, Then = s.Mode, Check = null, Notice = null },
                    s.Mode == Mode.Recording ? [Effect.PauseLog, Effect.StartTrying] : [Effect.StartTrying]);
            case ToggleTry when s.Mode == Mode.Trying:
                return Go(s with { Mode = s.Then }, s.Then == Mode.Recording ? [Effect.StopTrying, Effect.ResumeLog] : [Effect.StopTrying]);
            case ToggleTry:
                return Refuse(Why(s, "try things"));

            case CheckOn check when s.Check == check.Page:
                return Refuse("In check mode already.");
            case CheckOn check when s.Mode == Mode.Recording:
                return Go(s with { Check = check.Page, Inspect = null });
            case CheckOn:
                return Refuse(s.Mode switch
                {
                    Mode.Paused => "Paused: resume first (F8), then check.",
                    Mode.Trying => "Trying: record again first (F7), then check.",
                    _ => Why(s, "check"),
                });
            // Always taken, so a page that left check mode by itself, or a panel that thinks it is in it, agree again.
            case CheckOff:
                return Go(s with { Check = null });

            case InspectOn inspect when s.Inspect == inspect.Page:
                return Refuse("In inspect mode already.");
            case InspectOn inspect when s.Mode is Mode.Recording or Mode.Paused or Mode.Trying:
                return Go(s with { Inspect = inspect.Page, Check = null });
            case InspectOn:
                return Refuse(Why(s, "pick an element"));
            case InspectOff:
                return Go(s with { Inspect = null });
            case DocumentChanged changed:
                return Go(s with
                {
                    Check = SameDocument(s.Check, changed.Page) ? s.Check : null,
                    Inspect = SameDocument(s.Inspect, changed.Page) ? s.Inspect : null,
                });

            case Test when s.Mode is Mode.Recording or Mode.Paused or Mode.Trying && s.Part?.Phase is not (PartPhase.Replaying or PartPhase.Failed):
                return Go(s with { Mode = Mode.Testing, Then = s.Mode, Run = NewRun(s, RunKind.Test), Check = null, Inspect = null },
                    s.Mode == Mode.Recording ? [Effect.PauseLog, Effect.StartRun] : [Effect.StartRun]);
            case Test:
                return Refuse(Why(s, "test the flow"));

            case StopRun when s is { Mode: Mode.Testing, Run: { } run }:
                return Stopped(s, run);
            case StopRun:
                return Refuse("Nothing runs.");
            case RunEnded ended when s.Run is { } running && running.Id == ended.Id:
                return RunEnd(s, running, ended.Outcome);
            case RunEnded:
                return Refuse("That run ended already.");

            case Rerecord part when s.Mode is Mode.Recording or Mode.Paused or Mode.Trying && s.Part is null or { Phase: PartPhase.Done }:
                return Go(s with
                    {
                        Mode = Mode.Testing, Then = Mode.Paused, Run = NewRun(s, RunKind.PartReplay), Check = null, Inspect = null,
                        Part = new PartState(part.Label, part.After, PartPhase.Replaying, part.AtEnd, WasRecording: s.Mode is Mode.Recording or Mode.Trying),
                    },
                    s.Mode switch { Mode.Trying => [Effect.StopTrying, Effect.StartRun], Mode.Recording => [Effect.PauseLog, Effect.StartRun], _ => [Effect.StartRun] });
            case Rerecord when s.Part is { Phase: not PartPhase.Done }:
                return Refuse("A part is being recorded again: press Done (F2) or Esc first.");
            case Rerecord:
                return Refuse(Why(s, "record a part again"));
            case RerecordFromFailure when s is { Mode: Mode.Testing, Part.Phase: PartPhase.Failed, Run: null }:
                return Go(s with { Mode = Mode.Recording, Part = s.Part with { Phase = PartPhase.Recording, After = false } },
                    Effect.OpenPart, Effect.ResumeLogRebased);
            case RerecordFromFailure:
                return Refuse("The replay did not fail.");

            // Done keeps the part; in the middle of the flow the app is where the part left it, not at its end, so the
            // recording pauses, also from trying, and after a test running.
            case PartDone when s.Part is { Phase: PartPhase.Recording } part:
            {
                var done = s with { Part = part with { Phase = PartPhase.Done } };
                if (part.AtEnd) return Go(done, Effect.KeepPart);
                return s.Mode switch
                {
                    Mode.Recording => Go(done with { Mode = Mode.Paused, Then = Mode.Paused, Check = null, Inspect = null }, Effect.KeepPart, Effect.PauseLog),
                    Mode.Trying => Go(done with { Mode = Mode.Paused, Then = Mode.Paused }, Effect.KeepPart, Effect.StopTrying),
                    Mode.Testing => Go(done with { Then = Mode.Paused }, [Effect.KeepPart, .. Effects(s.Then == Mode.Trying, Effect.StopTrying)]),
                    _ => Go(done, Effect.KeepPart),
                };
            }
            case PartDone:
                return Refuse("No part is being recorded again.");
            case PartCancel when s.Part is { Phase: PartPhase.Replaying } && s.Run is { } replay:
                return Stopped(s, replay);
            case PartCancel when s.Part is { Phase: PartPhase.Failed }:
                return Go(s with { Mode = Mode.Paused, Part = null, Run = null, Then = Back(s.Part) }, Effect.GoBack);
            case PartCancel when s.Part is { Phase: PartPhase.Recording } part:
                return s.Mode switch
                {
                    Mode.Recording => Go(s with { Mode = Mode.Paused, Part = null, Check = null, Inspect = null, Then = Back(part) }, Effect.DropPart, Effect.PauseLog, Effect.GoBack),
                    Mode.Trying => Go(s with { Mode = Mode.Paused, Part = null, Then = Back(part) }, Effect.DropPart, Effect.StopTrying, Effect.GoBack),
                    Mode.Testing => Go(s with { Part = null, Then = Mode.Paused }, [Effect.DropPart, .. Effects(s.Then == Mode.Trying, Effect.StopTrying)]),
                    _ => Go(s with { Part = null, Then = Back(part) }, Effect.DropPart, Effect.GoBack),
                };
            case PartCancel:
                return Refuse("No part is being recorded again.");
            // Back where it was, the recording does what it did then; else it stays paused, as the app is elsewhere.
            case ReturnedToStart back when s.Mode == Mode.Paused:
                return back.Back && s.Then == Mode.Recording ? Go(s with { Mode = Mode.Recording }, Effect.ResumeLog)
                    : Go(s with { Then = Mode.Paused });
            case ReturnedToStart:
                return Go(s);
            case PartDismiss when s.Part is { Phase: PartPhase.Done }:
                return Go(s with { Part = null });
            case PartDismiss:
                return Refuse("No part was recorded again.");

            case KeepRecording keep when s.Mode == Mode.Asking:
                return keep.Keep ? Go(s with { Mode = Mode.Recording }, Effect.LeaveOutNotReplayed, Effect.ResumeLog)
                    : Go(s with { Mode = Mode.Saving, Stop = StopReason.Saved });
            case KeepRecording:
                return Refuse("Nothing is asked.");

            // From anything: the login, a test or a replay included, which the lifetime ends after the save. A flow
            // continued while it replays is saved as it was. A second stop is the lifetime's to force.
            case Stop stop when s.Mode != Mode.Saving:
                return Go(s with { Mode = Mode.Saving, Stop = stop.Reason, Check = null, Inspect = null },
                    [.. Effects(s.Mode == Mode.Trying, Effect.StopTrying), .. Effects(s.Mode == Mode.Recording, Effect.PauseLog)]);
            case Stop:
                return Refuse("The recording is being saved.");
            case Ended when s.Mode == Mode.Saving:
                return Go(s with { Mode = Mode.Ended, Run = null }, Effects(s.Run is not null, Effect.DisposeRun));
            case Ended:
                return Refuse("The recording is not being saved.");
        }

        return Refuse("Not a command of the recorder.");
    }

    // A run stopped by the person: it ends at once, and the state goes on as when it ended stopped.
    private static (RecorderState?, Effect[], string?) Stopped(RecorderState s, RunInfo run)
    {
        var (next, effects, refused) = RunEnd(s, run, RunOutcome.Stopped);
        return (next, [Effect.DisposeRun, .. effects.Where(effect => effect != Effect.DisposeRun)], refused);
    }

    private static (RecorderState?, Effect[], string?) RunEnd(RecorderState s, RunInfo run, RunOutcome outcome)
    {
        var ended = s with { Run = null };
        switch (run.Kind)
        {
            case RunKind.Login:
                return outcome == RunOutcome.Passed ? Go(ended with { Mode = Mode.Recording }, Effect.DisposeRun, Effect.ResumeLog)
                    : Go(ended with { Mode = Mode.Paused, Then = Mode.Paused, Notice = "The login flow did not log in: log in by hand, then resume." }, Effect.DisposeRun);
            case RunKind.Test:
                return Go(ended with { Mode = s.Then, Then = Mode.Recording }, s.Then == Mode.Recording ? [Effect.DisposeRun, Effect.ResumeLog] : [Effect.DisposeRun]);
            case RunKind.ContinueReplay:
                return outcome == RunOutcome.Passed ? Go(ended with { Mode = Mode.Recording }, Effect.DisposeRun, Effect.ResumeLog)
                    : Go(ended with { Mode = Mode.Asking }, Effect.DisposeRun);
            default:
                // The replay up to a part: there, the part is recorded; not got there, the person chooses; stopped, as it was.
                return outcome switch
                {
                    RunOutcome.Passed => Go(ended with { Mode = Mode.Recording, Part = s.Part! with { Phase = PartPhase.Recording } },
                        Effect.DisposeRun, Effect.OpenPart, Effect.ResumeLogRebased),
                    RunOutcome.Failed => Go(ended with { Part = s.Part! with { Phase = PartPhase.Failed } }, Effect.DisposeRun),
                    _ => Go(ended with { Mode = Mode.Paused, Part = null, Then = s.Part!.WasRecording ? Mode.Recording : Mode.Paused },
                        Effect.DisposeRun, Effect.GoBack),
                };
        }
    }

    private static Mode Back(PartState? part) => part?.WasRecording == true ? Mode.Recording : Mode.Paused;

    private static Effect[] Effects(bool when, Effect effect) => when ? [effect] : [];

    private static bool SameDocument(PageDocument? held, PageDocument changed) =>
        held is null || held.Page != changed.Page || held.Document == changed.Document;

    private static RunInfo NewRun(RecorderState s, RunKind kind) => new(kind, s.Epoch + 1);

    private static string Why(RecorderState s, string what) => s.Mode switch
    {
        Mode.Starting => $"Chrome is opening: {what} once it recorded its first page.",
        Mode.LoggingIn => $"The login flow runs: {what} once it logged in, or press Esc to log in by hand.",
        Mode.Testing => $"The flow runs: {what} once it ended, or press Esc to stop it.",
        Mode.Trying => $"Trying: press F7 to record again, then {what}.",
        Mode.Asking => $"Answer in the console first: keep recording, or stop.",
        Mode.Saving or Mode.Ended => "The recording is being saved.",
        _ => $"Not now: {what} later.",
    };

    private static (RecorderState?, Effect[], string?) Go(RecorderState next, params Effect[] effects) => (next, effects, null);

    private static (RecorderState?, Effect[], string?) Refuse(string why) => (null, [], why);
}

/// <summary>
/// The capture pipeline's side of a transition (pause-crash's <c>CapturePipeline</c> implements it): every page flushes
/// what it holds back (a field being typed, a composition, a click pending) and acknowledges it, or a short timeout is
/// recorded as such, before the state changes. Until it lands, <see cref="NoDrain"/> stands in.
/// </summary>
internal interface ICaptureDrain
{
    /// <summary>Drains every page of what began in <paramref name="epoch"/>; true when every page acknowledged it in time.</summary>
    Task<bool> DrainAsync(long epoch, CancellationToken cancel);
}

/// <summary>No pipeline to drain, as before the capture pipeline's handshake, and in the table's tests.</summary>
internal sealed class NoDrain : ICaptureDrain
{
    public Task<bool> DrainAsync(long epoch, CancellationToken cancel) => Task.FromResult(true);
}

/// <summary>Carries out a transition's effects: the recording session's side.</summary>
internal interface IEffects
{
    Task ApplyAsync(Effect effect, RecorderState before, RecorderState after);
}

/// <summary>
/// The only writer of the recorder's state. A request is checked against <see cref="RecorderTable"/>; refused, nothing
/// changes; accepted, the pages are drained first (<see cref="ICaptureDrain"/>), then the effects are carried out, then
/// the new snapshot is published with the next epoch. One request at a time, in the order they came. The console, the
/// panel and each page's overlay render from the snapshot only, and a page drops a snapshot older than the one it has.
/// A subscriber of <see cref="Published"/> only renders: a request from it would wait for the request publishing.
/// </summary>
internal sealed class RecorderStateMachine(ICaptureDrain drain, IEffects effects, RecorderState? start = null)
{
    private readonly SemaphoreSlim _turn = new(1, 1);
    private volatile RecorderState _state = start ?? RecorderState.Initial;

    public RecorderState State => _state;

    /// <summary>Raised with each snapshot, in epoch order, once its effects were carried out.</summary>
    public event Action<RecorderState>? Published;

    /// <summary>Asks for <paramref name="command"/>: the outcome says whether it was taken, and why not.</summary>
    public async Task<Outcome> RequestAsync(Command command, CancellationToken cancel = default)
    {
        await _turn.WaitAsync(cancel);
        try
        {
            var before = _state;
            var outcome = RecorderTable.Apply(before, command);
            if (!outcome.Accepted) return outcome;
            await drain.DrainAsync(before.Epoch, cancel);
            foreach (var effect in outcome.Effects) await effects.ApplyAsync(effect, before, outcome.State);
            _state = outcome.State;
            Published?.Invoke(outcome.State);
            return outcome;
        }
        finally
        {
            _turn.Release();
        }
    }
}
