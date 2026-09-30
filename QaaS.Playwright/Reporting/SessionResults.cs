using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Playwright.Reporting;

/// <summary>What the sessions attached to one assertion recorded.</summary>
/// <param name="SessionNames">The attached sessions.</param>
/// <param name="SessionOutcomes">Every flow outcome with the session it ran in, in run order.</param>
/// <param name="SessionFailures">Every failure the runner recorded for those sessions.</param>
/// <param name="UnverifiedSessions">Attached sessions that recorded neither a flow outcome nor a failure.</param>
/// <param name="Warnings">Every warning the probes logged, with the session it was logged in, each once.</param>
internal sealed record SessionResults(
    IReadOnlyList<string> SessionNames,
    IReadOnlyList<(string SessionName, PlaywrightFlowOutcome Outcome)> SessionOutcomes,
    IReadOnlyList<ActionFailure> SessionFailures,
    IReadOnlyList<string> UnverifiedSessions,
    IReadOnlyList<(string SessionName, string Warning)> Warnings)
{
    // How outcomes recorded outside a session are labelled; only a label, never a key they are looked up by.
    private const string Unscoped = "(unscoped)";

    public IReadOnlyList<PlaywrightFlowOutcome> Outcomes { get; } = [.. SessionOutcomes.Select(entry => entry.Outcome)];

    public bool Passed =>
        SessionNames.Count > 0 && UnverifiedSessions.Count == 0 && SessionFailures.Count == 0
        && Outcomes.All(outcome => outcome.Passed);

    public static SessionResults Collect(Context context, IReadOnlyCollection<SessionData> sessions)
    {
        var recorded = sessions
            .Select(session => (Session: session, Outcomes: PlaywrightFlowResults.Read(context, session.Name)))
            .ToList();

        // Outcomes recorded without a session scope cannot be attributed to a session, so they count for every
        // attached one: a failure among them fails the assertion, and any of them means flows did run.
        var unscoped = PlaywrightFlowResults.Read(context, sessionName: null);
        var unverified = recorded
            .Where(entry => unscoped.Count == 0 && entry.Outcomes.Count == 0 && entry.Session.SessionFailures.Count == 0)
            .Select(entry => entry.Session.Name);

        // Probes of one session, on one browser, often warn alike.
        var warnings = sessions.Select(session => (string?)session.Name).Append(null)
            .SelectMany(name => PlaywrightFlowResults.ReadWarnings(context, name).Select(warning => (name ?? Unscoped, warning)))
            .Distinct();

        return new SessionResults(
            [.. sessions.Select(session => session.Name)],
            [
                .. recorded.SelectMany(entry => entry.Outcomes.Select(outcome => (entry.Session.Name, outcome))),
                .. unscoped.Select(outcome => (Unscoped, outcome)),
            ],
            [.. sessions.SelectMany(session => session.SessionFailures)],
            [.. unverified],
            [.. warnings]);
    }
}
