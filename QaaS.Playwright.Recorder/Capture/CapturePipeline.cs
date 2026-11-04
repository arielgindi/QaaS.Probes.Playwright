namespace QaaS.Playwright.Recorder.Capture;

/// <summary>What the pipeline makes of a message of a page.</summary>
internal enum Verdict
{
    /// <summary>It began in an epoch that records: it is the recording's, however late it came.</summary>
    Record,

    /// <summary>It began in an epoch that records nothing: paused, trying, testing, logging in.</summary>
    Drop,

    /// <summary>It carries no epoch, as a message of a test or an older script does: the log's own rules decide.</summary>
    Unstamped,
}

/// <summary>What a drain found: the documents that acknowledged, and those that did not in time, as a page a browser dialog blocks.</summary>
internal sealed record DrainResult(IReadOnlyCollection<string> Acknowledged, IReadOnlyCollection<string> Late)
{
    public bool Complete => Late.Count == 0;
}

/// <summary>
/// The capture pipeline between the pages and the recording, by epochs rather than by clocks. An epoch is the number of
/// the recorder's state, as its state machine publishes it: each transition (pause, resume, try, test, a part recorded
/// again, the end) commits the next one. Each message of a page carries the epoch its action began in, as the page knew
/// it, and its document's sequence number; what began in an epoch that records is the recording's, whenever it comes,
/// and nothing else is. Before a transition commits, <see cref="DrainAsync"/> has each document flush what it holds back
/// (a field being typed, a composition, a press not yet a click) and waits until every message it sent before its
/// answer has come: then the transition, and a snapshot taken at the end, see all of it. Safe to call from any thread.
/// </summary>
internal sealed class CapturePipeline
{
    private readonly Lock _gate = new();
    private readonly Dictionary<long, bool> _records = [];
    private readonly Dictionary<string, Received> _documents = [];
    private long _epoch;

    /// <param name="recording">Whether the first epoch records: false while a login flow runs first, or a flow continued replays.</param>
    public CapturePipeline(bool recording = true) => _records[0] = recording;

    /// <summary>The epoch now, which the pages are told with the state.</summary>
    public long Epoch
    {
        get { lock (_gate) return _epoch; }
    }

    /// <summary>Whether what begins in the epoch now is recorded.</summary>
    public bool Recording
    {
        get { lock (_gate) return _records[_epoch]; }
    }

    /// <summary>Commits a transition: the next epoch, which records or not. Returns it.</summary>
    public long Commit(bool recording)
    {
        lock (_gate) return Commit(_epoch + 1, recording);
    }

    /// <summary>
    /// Commits a transition to <paramref name="epoch"/>, the number of the state machine's snapshot, which records or not.
    /// Epochs only go forward.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The epoch is not after the one now.</exception>
    public long Commit(long epoch, bool recording)
    {
        lock (_gate)
        {
            if (epoch <= _epoch) throw new ArgumentOutOfRangeException(nameof(epoch), epoch, $"Epochs go forward: the epoch now is {_epoch}.");
            (_records[epoch], _epoch) = (recording, epoch);
            return epoch;
        }
    }

    /// <summary>Whether what began in <paramref name="epoch"/> is recorded; an epoch never committed records nothing.</summary>
    public bool Records(long epoch)
    {
        lock (_gate) return _records.TryGetValue(epoch, out var records) && records;
    }

    /// <summary>
    /// Takes a message of <paramref name="document"/>: counts its sequence number, for a drain to wait for, and judges
    /// it by the epoch it began in.
    /// </summary>
    public Verdict Take(string document, long? epoch, long? sequence)
    {
        if (sequence is { } seq) Count(document, seq);
        if (epoch is not { } began) return Verdict.Unstamped;
        return Records(began) ? Verdict.Record : Verdict.Drop;
    }

    /// <summary>The highest sequence number of <paramref name="document"/> up to which every message came; 0 before the first.</summary>
    public long ReceivedFrom(string document)
    {
        lock (_gate) return _documents.TryGetValue(document, out var received) ? received.Through : 0;
    }

    /// <summary>
    /// Drains every document: <paramref name="flush"/> has each one send what it holds back and answer with the sequence
    /// number of its last message, or null when it does not answer; then waits, up to <paramref name="timeout"/>, until
    /// every message before each answer has come. A document that does not answer, or whose messages do not come in
    /// time, is late: its messages are still judged by their epoch when they come.
    /// </summary>
    public async Task<DrainResult> DrainAsync(Func<CancellationToken, Task<IReadOnlyDictionary<string, long?>>> flush,
        TimeSpan timeout, CancellationToken cancel = default)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        bounded.CancelAfter(timeout);
        IReadOnlyDictionary<string, long?> answers;
        try
        {
            answers = await flush(bounded.Token).WaitAsync(bounded.Token);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new DrainResult([], ["every document"]);
        }

        var waits = answers.Where(answer => answer.Value is not null)
            .Select(answer => (Document: answer.Key, Done: WaitForAsync(answer.Key, answer.Value!.Value))).ToList();
        try
        {
            await Task.WhenAll(waits.Select(wait => wait.Done)).WaitAsync(bounded.Token);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            // Some did not come in time: they are late.
        }

        var acknowledged = waits.Where(wait => wait.Done.IsCompletedSuccessfully).Select(wait => wait.Document).ToList();
        return new DrainResult(acknowledged, [.. answers.Keys.Except(acknowledged)]);
    }

    private void Count(string document, long sequence)
    {
        List<TaskCompletionSource> reached;
        lock (_gate)
        {
            if (!_documents.TryGetValue(document, out var received)) _documents[document] = received = new Received();
            reached = received.Add(sequence);
        }

        foreach (var waiter in reached) waiter.TrySetResult();
    }

    private Task WaitForAsync(string document, long sequence)
    {
        lock (_gate)
        {
            if (!_documents.TryGetValue(document, out var received)) _documents[document] = received = new Received();
            return received.WaitFor(sequence);
        }
    }

    // A document's sequence numbers as they came, in any order: up to which all came, those after a gap, and who waits.
    private sealed class Received
    {
        private readonly SortedSet<long> _ahead = [];
        private readonly List<(long Sequence, TaskCompletionSource Done)> _waiting = [];

        public long Through { get; private set; }

        public List<TaskCompletionSource> Add(long sequence)
        {
            if (sequence <= Through) return [];
            _ahead.Add(sequence);
            while (_ahead.Remove(Through + 1)) Through++;
            var reached = _waiting.Where(wait => wait.Sequence <= Through).ToList();
            _waiting.RemoveAll(wait => wait.Sequence <= Through);
            return [.. reached.Select(wait => wait.Done)];
        }

        public Task WaitFor(long sequence)
        {
            if (sequence <= Through) return Task.CompletedTask;
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiting.Add((sequence, done));
            return done.Task;
        }
    }
}
