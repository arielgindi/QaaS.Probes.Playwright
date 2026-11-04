using System.Text.Json;
using Microsoft.Playwright;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Generation;
using QaaS.Playwright.Recorder.State;
using QaaS.Playwright.Recorder.TestRun;

namespace QaaS.Playwright.Recorder.Panel;

/// <summary>
/// Recording a part of the flow again, or recording after an action, and trying things without recording them. A part
/// is recorded again in the recorded window itself: the flow replays there from its start, as a test runs it, up to
/// the part (<see cref="FlowTester.ReplayFromStartAsync"/>), nothing recorded meanwhile; then what the person does goes
/// in place of the part (<see cref="CaptureLog.BeginSplice"/>), until Done (F2 in the app) or Esc. When the replay does
/// not get there, the panel says where, as a test run does, and offers to record again from the step that failed.
/// While trying (F7), each action is shown as the recorder would write it, and Add to flow adds it where the next
/// action goes.
/// </summary>
internal sealed partial class RecorderPanel
{
    // The worker's own.
    private PartRecording? _part;
    private bool _returned;
    private IReadOnlyDictionary<string, PlannedAction> _triedPlanned = new Dictionary<string, PlannedAction>();
    private readonly HashSet<string> _triedAdded = [];

    private static bool IsPartCommand(PanelCommand command) =>
        command.Type is "rerecord" or "replayed" or "rerecordDone" or "rerecordCancel" or "rerecordFrom" or "rerecordDismiss"
            or "returned" or "try" or "addTried";

    private async Task PartAsync(PanelCommand command)
    {
        _refresh = true;
        if (Ending) return;
        switch (command.Type)
        {
            case "rerecord" when command.Action is { } id:
                await RerecordAsync(id, command.Step == true, command.After == true, command.Label ?? "the step");
                break;
            case "replayed" when _session.Snapshot.Run is { Kind: RunKind.PartReplay } replay:
                if (_tester.Report is { Outcome: not (TestOutcome.Passed or TestOutcome.Stopped) } failed && _part is { } part)
                    (part.Problem, part.FailedAt) = (failed.Outcome == TestOutcome.NotRun ? failed.Problem : null, failed.Failure?.Action);
                var ended = await _session.RequestAsync(new RunEnded(replay.Id, _tester.Report is { } report ? FlowTester.OutcomeOf(report) : RunOutcome.Stopped));
                if (ended.State.Part?.Phase == PartPhase.Failed) ConsoleUi.ReplayStopped(_part!.Part.Label);
                break;
            case "rerecordFrom":
                await RerecordFromFailureAsync();
                break;
            case "rerecordDone":
                Said(await _session.RequestAsync(new PartDone()));
                break;
            case "rerecordCancel":
                Said(await _session.RequestAsync(new PartCancel()));
                break;
            case "returned":
                await _session.RequestAsync(new ReturnedToStart(_returned));
                break;
            case "rerecordDismiss":
                await _session.RequestAsync(new PartDismiss());
                break;
            case "try" when command.On is null || command.On != (_session.State == RecordingState.Trying):
                Said(await _session.RequestAsync(new ToggleTry()));
                break;
            // Once: the same card added twice, as a double-click does, would be one action twice.
            case "addTried" when command.Action is { } id && !_triedAdded.Contains(id) && _session.Tried?.Build(_session.Options.Url) is { } tried:
                if (tried.Actions.FirstOrDefault(action => action.Id == id) is not { } action) break;
                var copy = JsonSerializer.Deserialize<CapturedAction>(JsonSerializer.Serialize(action, Recording.Json), Recording.Json)!;
                _session.Log.Insert(copy, afterId: null);
                _triedAdded.Add(id);
                break;
        }
    }

    // A command the state machine refused says why, once, as the panel's toast.
    private void Said(Outcome outcome)
    {
        if (!outcome.Accepted && outcome.Refused is { } why) Refuse(why);
    }

    // The flow replays from its start up to the part, in the recorded window, nothing recorded meanwhile.
    private async Task RerecordAsync(string id, bool wholeStep, bool after, string label)
    {
        if (_shown?.PartOf(id, wholeStep, after, label) is not { } part) return;
        // Where the app is now, to go back to when the part is cancelled before anything was recorded in it.
        var at = _session.PageNamed(_session.CurrentPage());
        var asked = await _session.RequestAsync(new Rerecord(part.Label, after, AtEnd: part.Before is null));
        if (!asked.Accepted)
        {
            Said(asked);
            return;
        }

        _part = new PartRecording(part) { ReturnTo = at is { IsClosed: false } ? (at, at.Url) : null };
        _namespace ??= ProjectNamespace.Of(Request.OutputDir);
        var request = Request;
        var namespaceName = _namespace;
        _ = Task.Run(async () =>
        {
            await _tester.ReplayFromStartAsync(request, namespaceName, part.UpTo);
            _commands.Writer.TryWrite(new PanelCommand("replayed", null, null, null));
        });
    }

    private async Task RerecordFromFailureAsync()
    {
        if (_part is not { FailedAt: { } failed } part || _shown is not { } shown) return;
        var order = shown.View.Steps.SelectMany(step => step.Actions).Select(action => action.Id).ToList();
        var (first, last) = (order.IndexOf(failed) + 1, order.IndexOf(part.Part.Last) + 1);
        if (shown.PartFrom(failed, part.Part, last > first ? $"steps {first} to {last}" : $"step {first}") is not { } from) return;
        var before = part.Part;
        part.Part = from;
        if (!(await _session.RequestAsync(new RerecordFromFailure())).Accepted) part.Part = before;
    }

    // What the state machine has the panel do of the part, in its turn: begin recording in its place, keep it, drop it,
    // or take the app back to the page it was on before the replay.
    private async Task PartEffectAsync(Effect effect, RecorderState before, RecorderState after)
    {
        if (_part is not { } part) return;
        switch (effect)
        {
            case Effect.OpenPart:
                _session.Log.BeginSplice(part.Part.Label, part.Part.Replaced, part.Part.After, part.Part.Before);
                _tester.Clear();
                ConsoleUi.Rerecording(part.Part.Label, after.Part?.After == true);
                break;
            case Effect.KeepPart:
                // What is being typed is part of it.
                if (_session.PageNamed(_session.CurrentPage()) is { } page) await PageElements.CallAsync(page, new { op = "flush" });
                _session.Log.EndSplice(keep: true);
                ConsoleUi.Rerecorded(part.Part.Label, kept: true, after.Mode == Mode.Paused || after.Then == Mode.Paused);
                break;
            case Effect.DropPart:
                _session.Log.EndSplice(keep: false);
                break;
            case Effect.GoBack:
                _returned = part.ReturnTo is var (at, url) && await GoBackAsync(at, url);
                _tester.Clear();
                ConsoleUi.Rerecorded(part.Part.Label, kept: false, paused: !_returned || after.Then != Mode.Recording, _returned ? part.ReturnTo!.Value.Url : null);
                _part = null;
                // Then the state machine hears how it went, in a turn of its own.
                _commands.Writer.TryWrite(new PanelCommand("returned", null, null, null));
                break;
        }
    }

    // The page back where it was before the replay, while the recording is still paused for it.
    private static async Task<bool> GoBackAsync(IPage page, string url)
    {
        try
        {
            if (page.IsClosed) return false;
            if (page.Url != url) await page.GotoAsync(url, new() { Timeout = 30_000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    // Undo took the part back before anything was recorded in it, or after it was done.
    private async Task UndoneAsync()
    {
        var phase = _session.Snapshot.Part?.Phase;
        if (phase == PartPhase.Recording && !_session.Log.Splicing) await _session.RequestAsync(new PartCancel());
        else if (phase == PartPhase.Done) await _session.RequestAsync(new PartDismiss());
    }

    // The part as the panel shows it.
    private PanelRerecord? PartView(PanelSnapshot shown, IReadOnlySet<string> fresh)
    {
        if (_session.Snapshot is not { Part: { } state } now || _part is not { } part) return null;
        var ids = shown.Recording.Actions.Select(action => action.Id).ToList();
        var planned = shown.View.Steps.SelectMany(step => step.Actions).Select(action => action.Id).ToHashSet();
        // Where what is recorded goes, while nothing is: after the last action of the flow before it.
        var before = part.Part.Before is { } next && ids.IndexOf(next) is >= 0 and var index ? index : ids.Count;
        var at = ids.Take(before).LastOrDefault(planned.Contains);
        return new PanelRerecord(part.Part.Label, state.Phase.ToString().ToLowerInvariant(), state.After)
        {
            Loop = part.Part.Loop, Fresh = [.. ids.Where(id => fresh.Contains(id) && planned.Contains(id))], At = at, FailedAt = part.FailedAt,
            Problem = part.Problem, Paused = now.Mode == Mode.Paused,
        };
    }

    // The actions tried, as the recorder would write them where the next action goes.
    private IReadOnlyList<PanelAction>? TriedView(PanelSnapshot shown)
    {
        if (_session.Tried is not { } log)
        {
            _triedAdded.Clear();
            _triedPlanned = new Dictionary<string, PlannedAction>();
            return null;
        }

        var tried = log.Build(_session.Options.Url);
        tried = tried with { Actions = [.. tried.Actions.Where(action => !_triedAdded.Contains(action.Id))] };
        var before = _session.Snapshot.Part?.Phase == PartPhase.Recording && _part is { } part ? part.Part.Before : null;
        var cards = PanelSnapshot.Tried(shown.Recording, tried, before, Request, _namespace ?? "Flows");
        _triedPlanned = cards.Where(card => card.Planned is not null).ToDictionary(card => card.Card.Id, card => card.Planned!);
        return [.. cards.Select(card => card.Card)];
    }

    /// <summary>
    /// What the panel knows of a part recorded again beside its phase, which is the state machine's: the actions it
    /// replaces and where what is recorded goes, why its replay failed, and where the app was when it began.
    /// </summary>
    private sealed record PartRecording(RerecordPart Part)
    {
        public RerecordPart Part { get; set; } = Part;
        public string? FailedAt { get; set; }
        public string? Problem { get; set; }

        /// <summary>The page the app was on when the part began, and its address.</summary>
        public (IPage Page, string Url)? ReturnTo { get; init; }
    }
}
