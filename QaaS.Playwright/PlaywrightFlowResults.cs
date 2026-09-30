using QaaS.Framework.SDK.ContextObjects;

namespace QaaS.Playwright;

/// <summary>
/// Carries flow outcomes from <see cref="PlaywrightFlowProbe"/> to <see cref="PlaywrightFlowAssertion"/> through the
/// run's global dictionary, one list per session, so a session never sees another session's results.
/// </summary>
public static class PlaywrightFlowResults
{
    /// <summary>The session name a probe records under when it runs outside a session.</summary>
    public const string UnscopedSessionName = "(unscoped)";

    private const string RootKey = "PlaywrightFlowResults";

    // Parallel sessions record at the same time; the lock covers only the list update, never browser work.
    private static readonly Lock Gate = new();

    public static void Record(Context context, string sessionName, PlaywrightFlowOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        lock (Gate)
        {
            var outcomes = Stored(context, sessionName);
            outcomes.Add(outcome);
            context.InsertValueIntoGlobalDictionary(PathOf(sessionName), outcomes);
        }
    }

    /// <summary>A copy of the session's outcomes, in the order the flows ran.</summary>
    public static IReadOnlyList<PlaywrightFlowOutcome> Read(Context context, string sessionName)
    {
        lock (Gate) return [.. Stored(context, sessionName)];
    }

    private static List<PlaywrightFlowOutcome> Stored(Context context, string sessionName)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);

        object? stored;
        try { stored = context.GetValueFromGlobalDictionary(PathOf(sessionName)); }
        catch (KeyNotFoundException) { return []; }

        return stored switch
        {
            null => [],
            List<PlaywrightFlowOutcome> outcomes => outcomes,
            _ => throw new InvalidOperationException(
                $"The global dictionary entry {RootKey}:{sessionName} holds a {stored.GetType().Name}, not flow " +
                "outcomes. Another component writes to that key."),
        };
    }

    private static List<string> PathOf(string sessionName) => [RootKey, sessionName];
}
