using System.Text.Json;
using Microsoft.Playwright;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Generation;
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
    private IReadOnlyDictionary<string, PlannedAction> _triedPlanned = new Dictionary<string, PlannedAction>();
    private readonly HashSet<string> _triedAdded = [];

    private static bool IsPartCommand(PanelCommand command) =>
        command.Type is "rerecord" or "replayed" or "rerecordDone" or "rerecordCancel" or "rerecordFrom" or "rerecordDismiss"
            or "try" or "addTried";

    private async Task PartAsync(PanelCommand command)
    {
        _refresh = true;
        if (_saving) return;
        switch (command.Type)
        {
            case "rerecord" when command.Action is { } id:
                Rerecord(id, command.Step == true, command.After == true, command.Label ?? "the step");
                break;
            case "replayed":
                await ReplayedAsync();
                break;
            case "rerecordFrom":
                RerecordFromFailure();
                break;
            case "rerecordDone":
                await EndPartAsync(keep: true);
                break;
            case "rerecordCancel":
                await EndPartAsync(keep: false);
                break;
            case "rerecordDismiss" when _part is { Phase: "done" }:
                _part = null;
                break;
            case "try" when command.On is null || command.On != (_session.State == RecordingState.Trying):
                _session.ToggleTry();
                break;
            case "addTried" when command.Action is { } id && _session.Tried?.Build(_session.Options.Url) is { } tried:
                if (tried.Actions.FirstOrDefault(action => action.Id == id) is not { } action) break;
                var copy = JsonSerializer.Deserialize<CapturedAction>(JsonSerializer.Serialize(action, Recording.Json), Recording.Json)!;
                _session.Log.Insert(copy, afterId: null);
                _triedAdded.Add(id);
                break;
        }
    }

    // The flow replays from its start up to the part, in the recorded window, nothing recorded meanwhile.
    private void Rerecord(string id, bool wholeStep, bool after, string label)
    {
        if (_part is { Phase: not "done" })
        {
            Refuse("A part is being recorded again: press Done (F2) or Esc first.");
            return;
        }

        if (_shown?.PartOf(id, wholeStep, after, label) is not { } part) return;
        var recording = _session.State is RecordingState.Recording or RecordingState.Trying;
        if (_session.State == RecordingState.Trying) _session.ToggleTry(resume: false);
        if (!_session.BeginTest())
        {
            Refuse("Wait for the login flow or the test to end first.");
            return;
        }

        // Where the app is now, to go back to when the part is cancelled before anything was recorded in it.
        var at = _session.PageNamed(_session.CurrentPage());
        _part = new PartRecording(part, after) { ReturnTo = at is { IsClosed: false } ? (at, at.Url) : null, WasRecording = recording };
        _session.Rerecording(after ? $"Recording after {part.Label}" : $"Re-recording {part.Label}");
        _namespace ??= ProjectNamespace.Of(Request.OutputDir);
        var request = Request;
        var namespaceName = _namespace;
        _ = Task.Run(async () =>
        {
            await _tester.ReplayFromStartAsync(request, namespaceName, part.UpTo);
            _commands.Writer.TryWrite(new PanelCommand("replayed", null, null, null));
        });
    }

    private async Task ReplayedAsync()
    {
        if (_part is not { Phase: "replaying" } part) return;
        var report = _tester.Report;
        switch (report?.Outcome)
        {
            case TestOutcome.Passed:
                Record(part.Part);
                break;
            case TestOutcome.Stopped or null:
                await EndedAsync(kept: false);
                break;
            default:
                // As a test run shows it, with the step it failed at; the person records again from there, or not.
                part.Phase = "failed";
                part.Problem = report.Outcome == TestOutcome.NotRun ? report.Problem : null;
                part.FailedAt = report.Failure?.Action;
                ConsoleUi.ReplayStopped(part.Part.Label);
                break;
        }
    }

    // The app is where the part starts: what the person does from now on goes in its place.
    private void Record(RerecordPart part)
    {
        _part!.Part = part;
        _part.Phase = "recording";
        _session.Log.BeginSplice(part.Label, part.Replaced, part.After, part.Before);
        _session.ResumeAfterReplay(_part.After ? $"Recording after {part.Label}" : $"Re-recording {part.Label}");
        _tester.Clear();
        ConsoleUi.Rerecording(part.Label, _part.After);
    }

    private void RerecordFromFailure()
    {
        if (_part is not { Phase: "failed", FailedAt: { } failed } part || _shown is not { } shown) return;
        var order = shown.View.Steps.SelectMany(step => step.Actions).Select(action => action.Id).ToList();
        var (first, last) = (order.IndexOf(failed) + 1, order.IndexOf(part.Part.Last) + 1);
        if (shown.PartFrom(failed, part.Part, last > first ? $"steps {first} to {last}" : $"step {first}") is not { } from) return;
        _part.After = false;
        Record(from);
    }

    // Done keeps what was recorded in the part, and Esc the part as it was. The recording then pauses, unless what was
    // recorded is the end of the flow, where the app now is.
    private async Task EndPartAsync(bool keep)
    {
        switch (_part?.Phase)
        {
            case "replaying":
                await _tester.StopAsync();
                break;
            case "failed":
                await EndedAsync(kept: false);
                break;
            case "recording":
                // What is being typed is part of it.
                if (keep && _session.PageNamed(_session.CurrentPage()) is { } page) await PageElements.CallAsync(page, new { op = "flush" });
                _session.Log.EndSplice(keep);
                await EndedAsync(kept: keep);
                break;
        }
    }

    // The part ended: kept, or as it was, by Esc, by Undo, or as the replay did not get to it. Cancelled before anything
    // was recorded in it, the app goes back to the page it was on, nothing recorded meanwhile, and the recording to what
    // it did before: recording again, or paused. Else it pauses where the app is not at the end of the flow, and says so.
    private async Task EndedAsync(bool kept)
    {
        if (_part is not { } part) return;
        var atEnd = kept && part.Part.Before is null;
        var back = false;
        if (!kept && part.ReturnTo is var (page, url))
        {
            if (part.Phase == "recording") _session.Pause();
            back = await GoBackAsync(page, url);
        }

        if (part.Phase is "replaying" or "failed") _session.EndTest(paused: !back);
        else if (back && part.WasRecording) _session.Resume();
        // Trying or testing too: once back from them, as the app is not where the flow ends.
        else if (!atEnd) _session.Pause();
        _session.Rerecording(null);
        if (part.Phase != "recording") _tester.Clear();
        var paused = _session.AfterTest == RecordingState.Paused;
        ConsoleUi.Rerecorded(part.Part.Label, kept, paused, back ? part.ReturnTo!.Value.Url : null);
        _part = kept ? part with { Phase = "done", Paused = paused } : null;
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
        if (_part is { Phase: "recording" } && !_session.Log.Splicing) await EndedAsync(kept: false);
        else if (_part is { Phase: "done" }) _part = null;
    }

    // The part as the panel shows it.
    private PanelRerecord? PartView(PanelSnapshot shown, IReadOnlySet<string> fresh)
    {
        if (_part is not { } part) return null;
        var ids = shown.Recording.Actions.Select(action => action.Id).ToList();
        var planned = shown.View.Steps.SelectMany(step => step.Actions).Select(action => action.Id).ToHashSet();
        // Where what is recorded goes, while nothing is: after the last action of the flow before it.
        var before = part.Part.Before is { } next && ids.IndexOf(next) is >= 0 and var index ? index : ids.Count;
        var at = ids.Take(before).LastOrDefault(planned.Contains);
        return new PanelRerecord(part.Part.Label, part.Phase, part.After)
        {
            Loop = part.Part.Loop, Fresh = [.. ids.Where(id => fresh.Contains(id) && planned.Contains(id))], At = at, FailedAt = part.FailedAt,
            Problem = part.Problem, Paused = part.Paused,
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
        var before = _part is { Phase: "recording" } part ? part.Part.Before : null;
        var cards = PanelSnapshot.Tried(shown.Recording, tried, before, Request, _namespace ?? "Flows");
        _triedPlanned = cards.Where(card => card.Planned is not null).ToDictionary(card => card.Card.Id, card => card.Planned!);
        return [.. cards.Select(card => card.Card)];
    }

    /// <summary>A part recorded again, or recorded after: how far it got.</summary>
    private sealed record PartRecording(RerecordPart Part, bool After)
    {
        public RerecordPart Part { get; set; } = Part;
        public bool After { get; set; } = After;

        /// <summary><c>replaying</c>, <c>failed</c>, <c>recording</c> or <c>done</c>.</summary>
        public string Phase { get; set; } = "replaying";

        public string? FailedAt { get; set; }
        public string? Problem { get; set; }
        public bool Paused { get; init; }

        /// <summary>The page the app was on when the part began, and its address.</summary>
        public (IPage Page, string Url)? ReturnTo { get; init; }

        /// <summary>The recording recorded when the part began, rather than being paused.</summary>
        public bool WasRecording { get; init; }
    }
}
