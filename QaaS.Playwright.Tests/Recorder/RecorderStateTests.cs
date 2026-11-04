using System.Diagnostics;
using QaaS.Playwright.Recorder.State;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>
/// The recorder's state table, explored whole: every state it can reach has a way out the person can press, says one
/// mode, records only while recording, and holds a run scope, a log, tried actions and a part's segment open exactly
/// while its state says so. The machine that applies it drains the pages before each change and publishes one epoch
/// after another, whoever asks at once. The cases that broke the recorder once are walked through the table.
/// </summary>
[TestFixture]
public class RecorderStateTests
{
    // What the effects hold open, as the session will: they must agree with the state after every transition.
    private sealed record Node(RecorderState State, bool LogOpen, bool RunOpen, bool Trying, bool PartOpen);

    [Test]
    public void EveryReachableState_HasAWayOut_SaysOneMode_AndHoldsOpenWhatItSays()
    {
        var start = new Node(RecorderState.Initial, false, false, false, false);
        var seen = new Dictionary<RecorderState, Node> { [Key(start.State)] = start };
        var edges = new List<(RecorderState From, RecorderState To)>();
        var queue = new Queue<Node>([start]);
        var problems = new List<string>();
        while (queue.Count > 0 && seen.Count < 20_000)
        {
            var node = queue.Dequeue();
            var state = node.State;
            foreach (var command in RecorderTable.Examples(state))
            {
                var outcome = RecorderTable.Apply(state, command);
                if (!outcome.Accepted)
                {
                    if (outcome.State != state || outcome.Refused is not { Length: > 0 }) problems.Add($"{Name(state)} + {command}: refused, yet changed");
                    continue;
                }

                if (outcome.State.Epoch != state.Epoch + 1) problems.Add($"{Name(state)} + {command}: epoch {outcome.State.Epoch}");
                var next = Follow(node, outcome);
                var why = Disagreement(next);
                if (why is not null) problems.Add($"{Name(state)} + {command} -> {Name(next.State)}: {why}");
                edges.Add((Key(state), Key(next.State)));
                if (seen.TryAdd(Key(next.State), next)) queue.Enqueue(next);
            }
        }

        // Every state but the end can get to recording, or to the end, by what the person presses and what happens.
        var toEnd = Reaching(edges, seen.Keys.Where(key => key.Mode == Mode.Ended));
        var toRecording = Reaching(edges, seen.Keys.Where(key => key.Mode == Mode.Recording));
        foreach (var state in seen.Keys)
        {
            if (state.Mode != Mode.Ended && !toEnd.Contains(state)) problems.Add($"{Name(state)}: never ends");
            if (state.Mode is not (Mode.Saving or Mode.Ended) && !toRecording.Contains(state)) problems.Add($"{Name(state)}: never records again");
            var allowed = RecorderTable.Allowed(state);
            if (state.Mode is not (Mode.Saving or Mode.Ended) && !allowed.Contains(nameof(Stop))) problems.Add($"{Name(state)}: cannot be saved");
            var view = RecorderView.Of(state);
            if (state.Mode is not (Mode.Recording or Mode.Saving or Mode.Ended or Mode.Starting) && view.Key is null && state.Part?.Phase != PartPhase.Done)
                problems.Add($"{Name(state)}: says {view.Label} without the key that leaves it");
            if (state.Records && view.Name is not ("Recording" or "Re-recording")) problems.Add($"{Name(state)}: records, but says {view.Label}");
            if (!state.Records && view.Name == "Recording") problems.Add($"{Name(state)}: says Recording, but records nothing");
        }

        Assert.Multiple(() =>
        {
            Assert.That(seen.Count, Is.InRange(40, 19_999), "the states reached");
            Assert.That(problems.Distinct().Take(30), Is.Empty);
        });
    }

    /// <summary>What broke the recorder once, walked through the table: the commands, then the mode it must be in.</summary>
    private static IEnumerable<TestCaseData> Cases() =>
    [
        Case("Done while trying pauses, not records at the flow's end", Mode.Paused,
            new Started(false, false), new Rerecord("FillAsync", After: false, AtEnd: false), Passed, new ToggleTry(), new PartDone()),
        Case("Done while a test runs pauses once it ended", Mode.Paused,
            new Started(false, false), new Rerecord("FillAsync", false, false), Passed, new Test(), new PartDone(), Passed),
        Case("Done at the flow's end goes on recording", Mode.Recording,
            new Started(false, false), new Rerecord("SaveAsync", After: true, AtEnd: true), Passed, new PartDone()),
        Case("Keep it as it was goes back to recording once the app is back", Mode.Recording,
            new Started(false, false), new Rerecord("FillAsync", false, false), Passed, new PartCancel(), new ReturnedToStart(true)),
        Case("Keep it as it was stays paused when the app could not go back", Mode.Paused,
            new Started(false, false), new Rerecord("FillAsync", false, false), Passed, new PartCancel(), new ReturnedToStart(false)),
        Case("Stop during the replay up to a part keeps it as it was", Mode.Paused,
            new Started(false, false), new Rerecord("FillAsync", false, false), new StopRun()),
        Case("Saving while a flow continued replays saves at once", Mode.Saving, new Started(false, true), new Stop(StopReason.Saved)),
        Case("Saving while the login flow runs ends it", Mode.Saving, new Started(true, false), new Stop(StopReason.Saved)),
        Case("A reload in check mode leaves it", Mode.Recording,
            new Started(false, false), new CheckOn(new("page", "d1")), new DocumentChanged(new("page", "d2"))),
        Case("Another tab's new document keeps check mode", Mode.Recording,
            new Started(false, false), new CheckOn(new("page", "d1")), new DocumentChanged(new("page1", "d7"))),
        Case("F8 while trying is refused, and says why", Mode.Trying, new Started(false, false), new ToggleTry(), new TogglePause()),
        Case("Trying from paused goes back to paused", Mode.Paused, new Started(false, false), new TogglePause(), new ToggleTry(), new ToggleTry()),
        Case("A test from trying goes back to trying", Mode.Trying, new Started(false, false), new ToggleTry(), new Test(), Passed),
        Case("Log in by hand pauses with why", Mode.Paused, new Started(true, false), new LogInByHand()),
        Case("The end of an older run changes nothing", Mode.Testing, new Started(false, false), new Test(), new StopRun(), new Test(), new RunEnded(2, RunOutcome.Passed)),
    ];

    private static readonly RunEnded Passed = new(-1, RunOutcome.Passed);

    [TestCaseSource(nameof(Cases))]
    public void AWayThroughTheTable_EndsInTheModeItMust(object expected, object commands)
    {
        // The table's types are the recorder's own, so its cases come as objects.
        var node = new Node(RecorderState.Initial, false, false, false, false);
        foreach (var asked in (Command[])commands)
        {
            // The end of a run is the one going on.
            var command = asked is RunEnded { Id: -1 } ended ? ended with { Id = node.State.Run?.Id ?? 0 } : asked;
            var outcome = RecorderTable.Apply(node.State, command);
            if (!outcome.Accepted) continue;
            node = Follow(node, outcome);
            Assert.That(Disagreement(node), Is.Null, $"after {command}");
        }

        Assert.That(node.State.Mode, Is.EqualTo((Mode)expected), Name(node.State));
    }

    [Test]
    public async Task TheMachine_DrainsBeforeEachChange_AndPublishesOneEpochAfterAnother_WhoeverAsksAtOnce()
    {
        var order = new List<string>();
        var machine = new RecorderStateMachine(new Drain(order), new Effects(order));
        var published = new List<long>();
        machine.Published += state => { lock (published) published.Add(state.Epoch); };
        await machine.RequestAsync(new Started(false, false));
        var random = new Random(7);
        for (var round = 0; round < 200; round++)
        {
            // Three askers at once, as the page, the panel and the console may.
            var asked = Enumerable.Range(0, 3).Select(_ => RecorderTable.Examples(machine.State).Where(command => command is not Started)
                .ElementAt(random.Next(25))).ToList();
            await Task.WhenAll(asked.Select(command => machine.RequestAsync(command)));
        }

        List<string> drains;
        lock (order) drains = [.. order];
        Assert.Multiple(() =>
        {
            Assert.That(published, Is.EqualTo(Enumerable.Range(1, published.Count).Select(epoch => (long)epoch)), "one epoch after another");
            Assert.That(machine.State.Epoch, Is.EqualTo(published.Count));
            // Each change drains the epoch it leaves before any of its effects.
            for (var at = 0; at < drains.Count; at++)
                if (drains[at].StartsWith("effect ", StringComparison.Ordinal))
                    Assert.That(drains.Take(at).Last(line => line.StartsWith("drain ", StringComparison.Ordinal)), Is.EqualTo($"drain {drains[at].Split(' ')[2]}"));
        });
    }

    [Test]
    public async Task ARunScope_EndsWhatItOwns_NewestFirst_InTheTimeItWasGiven_AndNeverThrows()
    {
        var ended = new List<string>();
        var scope = new RunScope("LoginFlow", within: TimeSpan.FromMilliseconds(600));
        scope.OnEnd("the page", () => { ended.Add("page"); return ValueTask.CompletedTask; });
        scope.Subscribe("its pop-ups", () => ended.Add("subscribed"), () => ended.Add("unsubscribed"));
        scope.OnEnd("a page that hangs", async () => await Task.Delay(Timeout.Infinite));
        scope.OnEnd("a flow that throws", () => throw new InvalidOperationException("disposed twice"));
        var waited = scope.Run(async stop => await Task.Delay(Timeout.Infinite, stop));
        _ = scope.Run(_ => Task.Delay(Timeout.Infinite));
        var sleeper = scope.Own(Process.Start(OperatingSystem.IsWindows() ? new ProcessStartInfo("ping", "-n 30 127.0.0.1") : new ProcessStartInfo("sleep", "30"))!);
        var clock = Stopwatch.StartNew();
        await scope.DisposeAsync();
        var took = clock.Elapsed;
        await scope.DisposeAsync();
        scope.OnEnd("a page opened after it ended", () => { ended.Add("late"); return ValueTask.CompletedTask; });
        await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(scope.Token.IsCancellationRequested || scope.Ended, Is.True);
            Assert.That(waited.IsCanceled, Is.True, "the token cancelled");
            Assert.That(sleeper.WaitForExit(2000), Is.True, "its process killed");
            Assert.That(ended, Is.EqualTo(new[] { "subscribed", "unsubscribed", "page", "late" }), "newest first, and the late one at once");
            Assert.That(took, Is.LessThan(TimeSpan.FromSeconds(2)), "within the time it was given");
            Assert.That(scope.Faults, Has.Some.Contains("a page that hangs").And.Some.Contains("disposed twice").And.Some.Contains("did not end within"));
        });
    }

    private static TestCaseData Case(string name, Mode expected, params Command[] commands) =>
        new TestCaseData(expected, commands).SetName(name.Replace(' ', '_').Replace(",", "").Replace("'", ""));

    // What the effects leave open after a transition.
    private static Node Follow(Node node, Outcome outcome)
    {
        var (log, run, trying, part) = (node.LogOpen, node.RunOpen, node.Trying, node.PartOpen);
        foreach (var effect in outcome.Effects)
            switch (effect)
            {
                case Effect.PauseLog: log = false; break;
                case Effect.ResumeLog or Effect.ResumeLogRebased: log = true; break;
                case Effect.StartRun: run = true; break;
                case Effect.DisposeRun: run = false; break;
                case Effect.StartTrying: trying = true; break;
                case Effect.StopTrying: trying = false; break;
                case Effect.OpenPart: part = true; break;
                case Effect.KeepPart or Effect.DropPart: part = false; break;
            }

        return new Node(outcome.State, log, run, trying, part);
    }

    private static string? Disagreement(Node node)
    {
        var s = node.State;
        if (node.LogOpen != (s.Mode == Mode.Recording) && s.Mode is not (Mode.Saving or Mode.Ended)) return $"log {(node.LogOpen ? "open" : "closed")}";
        if (node.RunOpen != s.Run is not null) return $"run scope {(node.RunOpen ? "left open" : "never started")}";
        var trying = s.Mode == Mode.Trying || s is { Mode: Mode.Testing, Then: Mode.Trying };
        if (node.Trying != trying && s.Mode is not (Mode.Saving or Mode.Ended)) return $"tried actions {(node.Trying ? "kept" : "not collected")}";
        if (node.PartOpen != (s.Part?.Phase == PartPhase.Recording) && s.Mode is not (Mode.Saving or Mode.Ended)) return $"part segment {(node.PartOpen ? "open" : "closed")}";
        return null;
    }

    // States told apart by what they are, not by their epoch or their run's number.
    private static RecorderState Key(RecorderState state) =>
        state with { Epoch = 0, Run = state.Run is null ? null : state.Run with { Id = 0 }, Notice = state.Notice is null ? null : "!" };

    private static HashSet<RecorderState> Reaching(List<(RecorderState From, RecorderState To)> edges, IEnumerable<RecorderState> targets)
    {
        var reached = targets.ToHashSet();
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var (from, to) in edges)
                if (reached.Contains(to) && reached.Add(from))
                    grew = true;
        }

        return reached;
    }

    private static string Name(RecorderState s) =>
        $"{s.Mode}{(s.Mode is Mode.Testing or Mode.Trying ? $"(then {s.Then})" : "")}{(s.Run is { } run ? $" run {run.Kind}" : "")}" +
        $"{(s.Part is { } part ? $" part {part.Phase}{(part.AtEnd ? " at end" : "")}" : "")}{(s.Check is not null ? " check" : "")}{(s.Inspect is not null ? " inspect" : "")}";

    private sealed class Drain(List<string> order) : ICaptureDrain
    {
        public async Task<bool> DrainAsync(long epoch, CancellationToken cancel)
        {
            await Task.Yield();
            lock (order) order.Add($"drain {epoch}");
            return true;
        }
    }

    private sealed class Effects(List<string> order) : IEffects
    {
        public Task ApplyAsync(Effect effect, RecorderState before, RecorderState after)
        {
            lock (order) order.Add($"effect {effect} {before.Epoch}");
            return Task.CompletedTask;
        }
    }
}
