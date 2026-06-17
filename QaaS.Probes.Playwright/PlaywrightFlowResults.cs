using QaaS.Framework.SDK.ContextObjects;

namespace QaaS.Probes.Playwright;

/// <summary>
/// The outcome of running a single Playwright flow.
/// </summary>
/// <param name="FlowName">The flow's class name, as listed in the probe configuration.</param>
/// <param name="Passed">True when the flow completed without throwing.</param>
/// <param name="FailureMessage">The failure reason when <paramref name="Passed"/> is false; otherwise null.</param>
/// <param name="FailureScreenshot">A PNG screenshot of the page at the moment of failure, when one could be
/// captured; otherwise null. Always null for a passing flow.</param>
public sealed record PlaywrightFlowOutcome(
    string FlowName,
    bool Passed,
    string? FailureMessage = null,
    byte[]? FailureScreenshot = null);

/// <summary>
/// The typed channel for per-flow results shared between <see cref="PlaywrightFlowProbe"/> (which records
/// them) and the PlaywrightFlowAssertion (which reports them). It is the single place that touches the
/// framework's run-scoped global dictionary, so the rest of the plugin stays fully typed.
/// </summary>
public static class PlaywrightFlowResults
{
    private static readonly List<string> GlobalDictionaryPath = ["PlaywrightFlowResults"];

    /// <summary>Appends one flow's outcome to the current run's results.</summary>
    public static void Record(Context context, PlaywrightFlowOutcome outcome)
    {
        var outcomes = ReadInternal(context);
        outcomes.Add(outcome);
        context.InsertValueIntoGlobalDictionary(GlobalDictionaryPath, outcomes);
    }

    /// <summary>Returns every recorded flow outcome for the current run, in execution order.</summary>
    public static IReadOnlyList<PlaywrightFlowOutcome> Read(Context context) => ReadInternal(context);

    // The framework's global dictionary is the one untyped boundary in the plugin: it stores values as
    // object?. We keep that boundary here and pattern-match back to the concrete list so callers stay typed.
    private static List<PlaywrightFlowOutcome> ReadInternal(Context context)
    {
        try
        {
            return context.GetValueFromGlobalDictionary(GlobalDictionaryPath) is List<PlaywrightFlowOutcome> recorded
                ? recorded
                : [];
        }
        catch (KeyNotFoundException)
        {
            return [];
        }
    }
}
