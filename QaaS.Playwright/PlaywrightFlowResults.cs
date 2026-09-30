using QaaS.Framework.SDK.ContextObjects;

namespace QaaS.Playwright;

/// <summary>
/// Carries flow outcomes from <see cref="PlaywrightFlowProbe"/> to <see cref="PlaywrightFlowAssertion"/> through the
/// run's global dictionary, one list per session, so a session never sees another session's results. A probe that runs
/// outside a session records apart from every session, whatever a session is called.
/// </summary>
public static class PlaywrightFlowResults
{
    private const string RootKey = "PlaywrightFlowResults";

    // Parallel sessions record at the same time; the lock covers only the list update, never browser work.
    private static readonly Lock Gate = new();

    /// <param name="context">The run's context.</param>
    /// <param name="sessionName">The session the flow ran in; null when its probe ran outside a session.</param>
    /// <param name="outcome">How the flow ended.</param>
    public static void Record(Context context, string? sessionName, PlaywrightFlowOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        lock (Gate)
        {
            var outcomes = Stored(context, sessionName);
            outcomes.Add(outcome);
            context.InsertValueIntoGlobalDictionary(PathOf(sessionName), outcomes);
        }
    }

    /// <summary>A copy of the session's outcomes, in the order the flows ran; null reads those recorded outside a session.</summary>
    public static IReadOnlyList<PlaywrightFlowOutcome> Read(Context context, string? sessionName)
    {
        lock (Gate) return [.. Stored(context, sessionName)];
    }

    private static List<PlaywrightFlowOutcome> Stored(Context context, string? sessionName)
    {
        ArgumentNullException.ThrowIfNull(context);

        object? stored;
        try { stored = context.GetValueFromGlobalDictionary(PathOf(sessionName)); }
        catch (KeyNotFoundException) { return []; }

        return stored switch
        {
            null => [],
            List<PlaywrightFlowOutcome> outcomes => outcomes,
            _ => throw new InvalidOperationException(
                $"The global dictionary entry {string.Join(':', PathOf(sessionName))} holds a {stored.GetType().Name}, not flow " +
                "outcomes. Another component writes to that key."),
        };
    }

    // A session's name is only ever a key under "Sessions", so no session name can reach the unscoped outcomes.
    private static List<string> PathOf(string? sessionName) =>
        sessionName is null ? [RootKey, "Unscoped"] : [RootKey, "Sessions", sessionName];
}
