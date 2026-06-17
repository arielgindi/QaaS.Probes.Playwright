using QaaS.Framework.SDK.ContextObjects;

namespace QaaS.Playwright;

/// <summary>
/// The typed channel that carries per-flow outcomes from <c>PlaywrightFlowProbe</c> (the writer) to
/// <c>PlaywrightFlowAssertion</c> (the reader).
///
/// Outcomes are isolated per session by the dictionary key, so a probe in one session never sees another
/// session's results. This class is the single boundary that touches the framework's untyped global dictionary;
/// a process-wide lock conservatively serializes the read-modify-write so the shared list cannot be corrupted by
/// parallel writers. The lock is held only around the in-memory list update (never around browser I/O), so the
/// conservative scope costs nothing in practice.
/// </summary>
public static class PlaywrightFlowResults
{
    /// <summary>
    /// The session key the probe falls back to when it runs without a session execution scope (i.e. outside the
    /// runner). The assertion also reads this bucket so a missing scope degrades gracefully rather than silently
    /// dropping outcomes.
    /// </summary>
    public const string UnscopedSessionName = "(unscoped)";

    private const string RootKey = "PlaywrightFlowResults";

    private static readonly Lock RecordGate = new();

    /// <summary>Records one flow's outcome for the given session.</summary>
    public static void Record(Context context, string sessionName, PlaywrightFlowOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
        ArgumentNullException.ThrowIfNull(outcome);

        lock (RecordGate)
        {
            var outcomes = ReadStored(context, sessionName);
            outcomes.Add(outcome);
            context.InsertValueIntoGlobalDictionary(PathFor(sessionName), outcomes);
        }
    }

    /// <summary>
    /// Returns a snapshot of the outcomes recorded for the given session, in execution order. The result is a
    /// copy, so callers cannot mutate the stored list.
    /// </summary>
    public static IReadOnlyList<PlaywrightFlowOutcome> Read(Context context, string sessionName)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);

        lock (RecordGate)
        {
            return ReadStored(context, sessionName).ToArray();
        }
    }

    // Returns the live stored list — only ever called while holding RecordGate, so the caller must snapshot
    // (Read) before exposing it.
    private static List<PlaywrightFlowOutcome> ReadStored(Context context, string sessionName)
    {
        var stored = TryGetStored(context, sessionName);
        return stored switch
        {
            null => [],
            List<PlaywrightFlowOutcome> outcomes => outcomes,
            _ => throw new InvalidOperationException(
                $"The Playwright results channel for session '{sessionName}' holds an unexpected " +
                $"'{stored.GetType().Name}'. Another component is writing to the '{RootKey}' global-dictionary key."),
        };
    }

    private static object? TryGetStored(Context context, string sessionName)
    {
        try
        {
            return context.GetValueFromGlobalDictionary(PathFor(sessionName));
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private static List<string> PathFor(string sessionName) => [RootKey, sessionName];
}
