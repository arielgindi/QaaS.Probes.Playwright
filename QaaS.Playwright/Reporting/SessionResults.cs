using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Playwright.Reporting;

/// <summary>What the sessions attached to one assertion recorded.</summary>
/// <param name="SessionNames">The attached sessions.</param>
/// <param name="Outcomes">Every flow outcome, in run order.</param>
/// <param name="SessionFailures">Every failure the runner recorded for those sessions.</param>
/// <param name="UnverifiedSessions">Attached sessions that recorded neither a flow outcome nor a failure.</param>
internal sealed record SessionResults(
    IReadOnlyList<string> SessionNames,
    IReadOnlyList<PlaywrightFlowOutcome> Outcomes,
    IReadOnlyList<ActionFailure> SessionFailures,
    IReadOnlyList<string> UnverifiedSessions)
{
    public bool Passed =>
        SessionNames.Count > 0 && UnverifiedSessions.Count == 0 && SessionFailures.Count == 0
        && Outcomes.All(outcome => outcome.Passed);

    public static SessionResults Collect(Context context, IReadOnlyCollection<SessionData> sessions)
    {
        // Outcomes recorded without a session scope cannot be attributed to a session, so they count for every
        // attached one: a failure among them fails the assertion, and any of them means flows did run.
        var unscoped = PlaywrightFlowResults.Read(context, PlaywrightFlowResults.UnscopedSessionName);
        var scoped = sessions.Select(session => PlaywrightFlowResults.Read(context, session.Name)).ToList();
        var unverified = sessions
            .Where((session, index) => scoped[index].Count == 0 && session.SessionFailures.Count == 0)
            .Select(session => session.Name);

        return new SessionResults(
            [.. sessions.Select(session => session.Name)],
            [.. scoped.SelectMany(outcomes => outcomes), .. unscoped],
            [.. sessions.SelectMany(session => session.SessionFailures)],
            unscoped.Count > 0 ? [] : [.. unverified]);
    }
}
