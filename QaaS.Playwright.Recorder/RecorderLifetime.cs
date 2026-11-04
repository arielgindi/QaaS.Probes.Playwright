namespace QaaS.Playwright.Recorder;

/// <summary>Why a recording ends.</summary>
internal enum StopReason
{
    /// <summary>Save in the panel.</summary>
    Saved,

    /// <summary>Ctrl+C in the console, the first time: it ends the recording as Save does.</summary>
    CtrlC,

    /// <summary>The person closed Chrome, or it went away.</summary>
    BrowserClosed,

    /// <summary>Ctrl+C again while the recording ends, or before it began: it stops at once, the recording kept.</summary>
    Interrupted,

    /// <summary>SIGTERM, as <c>kill</c> sends.</summary>
    Terminated,

    /// <summary>SIGHUP, as a closed terminal sends.</summary>
    HungUp,

    /// <summary>SIGQUIT, or Ctrl+Break on Windows.</summary>
    Quit,

    /// <summary>An error nothing caught.</summary>
    Crashed,
}

/// <summary>
/// What a stop does: <paramref name="Save"/> writes the flow, whose result is the exit code; else the recording is kept, to
/// save the next time the recorder starts, and the recorder ends with <paramref name="ExitCode"/>, the shell's code for
/// the signal. <paramref name="Logged"/> writes what happened, with its stack, to the error log.
/// </summary>
internal sealed record StopPlan(bool Save, int ExitCode, bool Logged);

/// <summary>
/// The one owner of how a recording ends. Ctrl+C, a signal, Chrome closed, the panel's Save and an error nothing
/// caught each become a <see cref="RequestStop"/> with its <see cref="StopReason"/>, and every end then takes the same
/// ordered path (<see cref="ShutdownAsync"/>): what the pages hold back is drained into the recording, the recording is
/// snapshotted, the flow is saved or the recording kept, the run scopes are disposed newest first, the child processes
/// are killed, and what happened is logged. A second request while it ends forces it: what is not done yet is skipped
/// but the snapshot, and the recorder ends with the second request's exit code. Every step is bounded, so a page or a
/// run that does not answer never keeps the recorder from ending.
/// </summary>
internal sealed class RecorderLifetime
{
    /// <summary>How long a step may take before the shutdown goes on without it.</summary>
    internal static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(3);

    private readonly Lock _gate = new();
    private readonly TaskCompletionSource<StopReason> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _ending = new();
    private readonly CancellationTokenSource _forced = new();
    private readonly List<(string Name, Func<ValueTask> Dispose)> _scopes = [];
    private readonly Action<int> _exit;
    private readonly Func<string, Exception?, bool> _log;
    private readonly TimeSpan _stepTimeout;
    private Func<CancellationToken, Task>? _drain;
    private Func<CancellationToken, Task<bool>>? _snapshot;
    private StopReason? _first;
    private StopReason? _forcedBy;
    private int _snapshotTaken;
    private volatile bool _done;

    /// <param name="exit">Ends the process with a code, when a forced stop cannot wait for the shutdown to return.</param>
    /// <param name="log">Writes what happened, with its stack, to the error log; <see cref="CrashReport.Log"/>.</param>
    /// <param name="stepTimeout">How long each step may take; <see cref="StepTimeout"/> unless a test says otherwise.</param>
    public RecorderLifetime(Action<int>? exit = null, Func<string, Exception?, bool>? log = null, TimeSpan? stepTimeout = null)
    {
        _exit = exit ?? Environment.Exit;
        _log = log ?? CrashReport.Log;
        _stepTimeout = stepTimeout ?? StepTimeout;
    }

    /// <summary>The lifetime of the recording going on, which Ctrl+C, a signal and an error nothing caught stop; else null.</summary>
    public static RecorderLifetime? Current { get; set; }

    /// <summary>The child processes the recording started, killed as it ends.</summary>
    public ChildProcesses Children { get; } = new();

    /// <summary>Cancelled once the shutdown disposes the run scopes: what a login, a replay, a test or a build waits for.</summary>
    public CancellationToken Ending => _ending.Token;

    /// <summary>Completes with the reason of the first request to stop.</summary>
    public Task<StopReason> Stopped => _stopped.Task;

    /// <summary>
    /// Whether a stop now ends the recording as Save does. Before the recording records (logging in, replaying, a
    /// question in the panel), nothing waits for <see cref="Stopped"/>: Ctrl+C then stops at once, the recording kept.
    /// </summary>
    public bool Listening { get; set; }

    /// <summary>What each stop does, as one table.</summary>
    public static StopPlan PlanFor(StopReason reason) => reason switch
    {
        StopReason.Saved or StopReason.CtrlC or StopReason.BrowserClosed => new(Save: true, ExitCode: 0, Logged: false),
        StopReason.Interrupted => new(Save: false, ExitCode: 130, Logged: false),
        StopReason.Terminated => new(Save: false, ExitCode: 143, Logged: true),
        StopReason.HungUp => new(Save: false, ExitCode: 129, Logged: true),
        StopReason.Quit => new(Save: false, ExitCode: 131, Logged: true),
        StopReason.Crashed => new(Save: false, ExitCode: 1, Logged: true),
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "A reason to stop the table does not have."),
    };

    /// <summary>How the pages' held-back input (a field being typed, a composition) is drained into the recording.</summary>
    public void OnDrain(Func<CancellationToken, Task> drain) => _drain = drain;

    /// <summary>How the recording is written as it is, to keep it; it returns whether there was anything to keep.</summary>
    public void OnSnapshot(Func<CancellationToken, Task<bool>> snapshot) => _snapshot = snapshot;

    /// <summary>A run scope to dispose as the recording ends, after those registered later.</summary>
    public void Own(string name, Func<ValueTask> dispose)
    {
        lock (_gate) _scopes.Add((name, dispose));
    }

    /// <summary>What a request to stop did.</summary>
    internal enum Outcome
    {
        /// <summary>The recording ends for this reason.</summary>
        Ending,

        /// <summary>It was ending already: this request forces it.</summary>
        Forced,

        /// <summary>It was ending already, and this request does not force it: Save or Chrome closed again.</summary>
        Ignored,
    }

    /// <summary>
    /// Asks the recording to end. The first request is the reason it ends for; Ctrl+C then, before anything listens,
    /// stops at once (<see cref="StopReason.Interrupted"/>). A later Ctrl+C, signal or error forces the end.
    /// </summary>
    public Outcome RequestStop(StopReason reason)
    {
        lock (_gate)
        {
            if (_first is null)
            {
                _first = reason == StopReason.CtrlC && !Listening ? StopReason.Interrupted : reason;
                _stopped.TrySetResult(_first.Value);
                return Outcome.Ending;
            }

            if (_forcedBy is not null || reason is StopReason.Saved or StopReason.BrowserClosed) return Outcome.Ignored;
            _forcedBy = reason == StopReason.CtrlC ? StopReason.Interrupted : reason;
        }

        _forced.Cancel();
        return Outcome.Forced;
    }

    /// <summary>
    /// Ends the recording for the reason <see cref="Stopped"/> gave, in order: drain, snapshot, save (with
    /// <paramref name="save"/>, which returns the exit code) or keep, dispose the run scopes, kill the child processes,
    /// log. Returns the exit code. A forced stop skips what is left but the snapshot, and ends the process itself when
    /// the save does not give way in time.
    /// </summary>
    /// <param name="save">Writes the flow from the snapshot; its token is cancelled when the stop is forced.</param>
    /// <param name="failure">For <see cref="StopReason.Crashed"/>: what happened.</param>
    public async Task<int> ShutdownAsync(Func<CancellationToken, Task<int>> save, Exception? failure = null)
    {
        var reason = await Stopped;
        var plan = PlanFor(reason);
        var forced = _forced.Token;
        // A forced stop that a save does not give way to ends the process anyway, once the snapshot is taken.
        await using var watchdog = forced.Register(() => _ = ForceAsync());

        await BoundedAsync("drain", _drain, forced);
        await SnapshotAsync();
        var exit = plan.ExitCode;
        var saved = false;
        if (plan.Save && !forced.IsCancellationRequested)
        {
            try
            {
                exit = await save(forced);
                saved = true;
            }
            catch (OperationCanceledException) when (forced.IsCancellationRequested)
            {
                // Forced: the recording is kept instead.
            }
        }

        // Forced once the save was done, it ends as the save says.
        if (_forcedBy is { } by && !saved)
        {
            reason = by;
            plan = PlanFor(by);
            exit = plan.ExitCode;
        }

        await DisposeScopesAsync();
        Children.KillAll();
        if (plan.Logged) _log($"The recording ended: {reason}", failure);
        _done = true;
        return exit;
    }

    /// <summary>
    /// For an end that cannot wait for <see cref="ShutdownAsync"/>, as an error nothing caught, whose process the runtime
    /// ends once its handlers return: the snapshot, the child processes killed and the log, bounded, synchronously.
    /// Returns whether there was anything to keep.
    /// </summary>
    public bool Emergency(StopReason reason, Exception? failure = null)
    {
        RequestStop(reason);
        var kept = false;
        try
        {
            var snapshot = SnapshotAsync();
            if (snapshot.Wait(_stepTimeout)) kept = snapshot.Result;
        }
        catch (Exception notKept) when (notKept is not OutOfMemoryException)
        {
            _log("Could not keep the recording", notKept);
        }

        Children.KillAll();
        if (PlanFor(reason).Logged) _log($"The recording ended: {reason}", failure);
        return kept;
    }

    // The snapshot is taken once, whichever path ends the recording first.
    private async Task<bool> SnapshotAsync()
    {
        if (Interlocked.Exchange(ref _snapshotTaken, 1) == 1 || _snapshot is not { } snapshot) return false;
        using var bounded = new CancellationTokenSource(_stepTimeout);
        try
        {
            return await snapshot(bounded.Token).WaitAsync(_stepTimeout);
        }
        catch (Exception notKept) when (notKept is not OutOfMemoryException)
        {
            _log("Could not keep the recording", notKept);
            return false;
        }
    }

    private async Task ForceAsync()
    {
        await SnapshotAsync();
        Children.KillAll();
        // The shutdown has that long to return the forced code itself, as the save may not watch its token.
        await Task.Delay(_stepTimeout * 2);
        if (!_done) _exit(PlanFor(_forcedBy ?? StopReason.Interrupted).ExitCode);
    }

    private async Task DisposeScopesAsync()
    {
        await _ending.CancelAsync();
        List<(string Name, Func<ValueTask> Dispose)> scopes;
        lock (_gate)
        {
            scopes = [.. _scopes];
            _scopes.Clear();
        }

        foreach (var (name, dispose) in Enumerable.Reverse(scopes))
            await BoundedAsync(name, async _ => await dispose(), CancellationToken.None);
    }

    // A step that throws or takes too long is logged and passed: the recorder still ends.
    private async Task BoundedAsync(string name, Func<CancellationToken, Task>? step, CancellationToken forced)
    {
        if (step is null || forced.IsCancellationRequested) return;
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(forced);
        bounded.CancelAfter(_stepTimeout);
        try
        {
            await step(bounded.Token).WaitAsync(bounded.Token);
        }
        catch (OperationCanceledException)
        {
            // Forced, or too long: the next step.
        }
        catch (Exception failed) when (failed is not OutOfMemoryException)
        {
            _log($"The recording's {name} failed as it ended", failed);
        }
    }
}
