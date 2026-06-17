using QaaS.Framework.SDK.ContextObjects;

namespace QaaS.Probes.Playwright;

/// <summary>
/// The typed, session-scoped channel that carries per-flow outcomes from <c>PlaywrightFlowProbe</c>
/// (the writer) to <c>PlaywrightFlowAssertion</c> (the reader).
///
/// Outcomes are stored in the run-scoped <see cref="Context"/> global dictionary under a path that
/// includes the session name, so concurrent probes in different sessions never see each other's
/// results. This class is the single boundary that touches the framework's untyped global dictionary;
/// every read-modify-write is serialized so the shared list cannot be corrupted by parallel writers.
/// </summary>
public static class PlaywrightFlowResults
{
    private const string RootKey = "PlaywrightFlowResults";

    // The global dictionary is shared mutable state, and recording an outcome is a read-modify-write
    // across two dictionary calls. Serialize it so interleaved probes cannot lose or mix outcomes.
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
    /// Returns a snapshot of the outcomes recorded for the given session, in execution order.
    /// The result is a copy, so callers cannot mutate the stored list.
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
