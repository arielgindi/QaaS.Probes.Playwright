using QaaS.Framework.SDK.ContextObjects;

namespace QaaS.Playwright;

/// <summary>
/// Carries flow outcomes, and the warnings the probe logged, from <see cref="PlaywrightFlowProbe"/> to
/// <see cref="PlaywrightFlowAssertion"/> through the run's global dictionary, one list per session, so a session never
/// sees another session's results. A probe that runs outside a session records apart from every session, whatever a
/// session is called.
/// </summary>
public static class PlaywrightFlowResults
{
    private const string OutcomesKey = "PlaywrightFlowResults";
    private const string WarningsKey = "PlaywrightFlowWarnings";

    // Parallel sessions record at the same time; the lock covers only the list update, never browser work.
    private static readonly Lock Gate = new();

    /// <param name="context">The run's context.</param>
    /// <param name="sessionName">The session the flow ran in; null when its probe ran outside a session.</param>
    /// <param name="outcome">How the flow ended.</param>
    public static void Record(Context context, string? sessionName, PlaywrightFlowOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        Add(context, PathOf(OutcomesKey, sessionName), outcome);
    }

    /// <summary>
    /// A copy of the session's outcomes, in the order the flows ran; a null session reads those recorded outside one.
    /// </summary>
    public static IReadOnlyList<PlaywrightFlowOutcome> Read(Context context, string? sessionName) =>
        Copy<PlaywrightFlowOutcome>(context, PathOf(OutcomesKey, sessionName));

    internal static void RecordWarning(Context context, string? sessionName, string warning) =>
        Add(context, PathOf(WarningsKey, sessionName), warning);

    internal static IReadOnlyList<string> ReadWarnings(Context context, string? sessionName) =>
        Copy<string>(context, PathOf(WarningsKey, sessionName));

    private static void Add<T>(Context context, List<string> path, T item)
    {
        lock (Gate)
        {
            var items = Stored<T>(context, path);
            items.Add(item);
            context.InsertValueIntoGlobalDictionary(path, items);
        }
    }

    private static IReadOnlyList<T> Copy<T>(Context context, List<string> path)
    {
        lock (Gate) return [.. Stored<T>(context, path)];
    }

    private static List<T> Stored<T>(Context context, List<string> path)
    {
        ArgumentNullException.ThrowIfNull(context);

        object? stored;
        try { stored = context.GetValueFromGlobalDictionary(path); }
        catch (KeyNotFoundException) { return []; }

        return stored switch
        {
            null => [],
            List<T> items => items,
            _ => throw new InvalidOperationException(
                $"The global dictionary entry {string.Join(':', path)} holds a {stored.GetType().Name}, not a list of " +
                $"{typeof(T).Name}. Another component writes to that key."),
        };
    }

    // A session's name is only ever a key under "Sessions", so no session name can reach what was recorded outside one.
    private static List<string> PathOf(string rootKey, string? sessionName) =>
        sessionName is null ? [rootKey, "Unscoped"] : [rootKey, "Sessions", sessionName];
}
