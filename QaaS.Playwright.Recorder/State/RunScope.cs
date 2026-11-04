using System.Diagnostics;

namespace QaaS.Playwright.Recorder.State;

/// <summary>
/// Everything one run of user code uses (a login flow, a test, a replay, an inspection of a flow's settings): its token,
/// the pages it opened, its event subscriptions, its flow instances and its processes. Disposing it, as leaving the state
/// that runs it does, cancels the token, then ends what it owns, newest first, in bounded time: whatever the code waits
/// for fails at once, and nothing it does reaches the recording after. What failed, or did not end in time, is in
/// <see cref="Faults"/>; disposing never throws, and never waits longer than it was given.
/// </summary>
internal sealed class RunScope(string name, TimeSpan? within = null) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly TimeSpan _within = within ?? TimeSpan.FromSeconds(5);
    private readonly Lock _gate = new();
    private readonly List<(string What, Func<ValueTask> End)> _owned = [];
    private readonly List<Task> _work = [];
    private readonly List<string> _faults = [];
    private Task? _disposing;

    public string Name => name;

    /// <summary>Cancelled as the scope ends.</summary>
    public CancellationToken Token => _stop.Token;

    public bool Ended
    {
        get { lock (_gate) return _disposing is not null; }
    }

    /// <summary>What did not end well, or in time, as the scope ended.</summary>
    public IReadOnlyList<string> Faults
    {
        get { lock (_faults) return [.. _faults]; }
    }

    /// <summary>
    /// Ends <paramref name="end"/> with the scope; given once the scope ended (a page a flow opened after Stop), at once.
    /// </summary>
    public void OnEnd(string what, Func<ValueTask> end)
    {
        lock (_gate)
            if (_disposing is null)
            {
                _owned.Add((what, end));
                return;
            }

        _ = EndOneAsync(what, end, _within);
    }

    /// <summary>Disposes <paramref name="resource"/> with the scope, and returns it.</summary>
    public T Own<T>(T resource, string? what = null) where T : class
    {
        var named = what ?? resource.GetType().Name;
        switch (resource)
        {
            case IAsyncDisposable async:
                OnEnd(named, async.DisposeAsync);
                break;
            case IDisposable disposable:
                OnEnd(named, () =>
                {
                    disposable.Dispose();
                    return ValueTask.CompletedTask;
                });
                break;
            default:
                throw new ArgumentException($"{named} is neither disposable nor a process.", nameof(resource));
        }

        return resource;
    }

    /// <summary>Kills <paramref name="process"/>, and what it started, with the scope.</summary>
    public Process Own(Process process)
    {
        OnEnd($"process {process.StartInfo.FileName}", () =>
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It ended meanwhile.
            }

            return ValueTask.CompletedTask;
        });
        return process;
    }

    /// <summary>Subscribes now, and unsubscribes with the scope, whether or not it owns what it subscribed to.</summary>
    public void Subscribe(string what, Action subscribe, Action unsubscribe)
    {
        subscribe();
        OnEnd(what, () =>
        {
            unsubscribe();
            return ValueTask.CompletedTask;
        });
    }

    /// <summary>
    /// Runs <paramref name="work"/> with the scope's token. The scope waits for it as it ends, for as long as it may, and
    /// keeps what it threw, other than its cancellation, in <see cref="Faults"/>: it is never left unobserved.
    /// </summary>
    public Task Run(Func<CancellationToken, Task> work)
    {
        var running = Task.Run(() => work(_stop.Token));
        lock (_gate) _work.Add(running);
        return running;
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) _disposing ??= EndAsync();
        return new ValueTask(_disposing);
    }

    private async Task EndAsync()
    {
        var until = DateTime.UtcNow + _within;
        try
        {
            await _stop.CancelAsync();
        }
        catch (AggregateException failure)
        {
            Fault($"{name}: a callback of its cancellation threw: {failure.InnerException?.Message}");
        }

        List<(string What, Func<ValueTask> End)> owned;
        List<Task> work;
        lock (_gate) (owned, work) = ([.. _owned], [.. _work]);
        owned.Reverse();
        foreach (var (what, end) in owned) await EndOneAsync(what, end, Left(until));

        var all = Task.WhenAll(work);
        if (await Task.WhenAny(all, Task.Delay(Left(until))) != all)
            Fault($"{name} did not end within {_within.TotalSeconds:0.#} s; what it does now reaches nothing it owned.");
        foreach (var failed in work.Where(task => task.IsFaulted))
            Fault($"{name} failed: {failed.Exception!.InnerException?.Message}");
        foreach (var going in work.Where(task => !task.IsCompleted))
            _ = going.ContinueWith(task => _ = task.Exception, TaskScheduler.Default);
    }

    private async Task EndOneAsync(string what, Func<ValueTask> end, TimeSpan left)
    {
        try
        {
            var ending = end().AsTask();
            if (await Task.WhenAny(ending, Task.Delay(left)) != ending)
            {
                Fault($"{what} of {name} did not end in time.");
                _ = ending.ContinueWith(task => _ = task.Exception, TaskScheduler.Default);
                return;
            }

            await ending;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Fault($"{what} of {name}: {failure.Message}");
        }
    }

    private static TimeSpan Left(DateTime until) => until - DateTime.UtcNow is { Ticks: > 0 } left ? left : TimeSpan.Zero;

    private void Fault(string fault)
    {
        lock (_faults) _faults.Add(fault);
    }
}
