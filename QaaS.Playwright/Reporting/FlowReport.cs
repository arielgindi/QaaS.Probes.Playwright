using System.Text;

namespace QaaS.Playwright.Reporting;

/// <summary>The report text of <see cref="PlaywrightFlowAssertion"/>: a one-line message and a detailed trace.</summary>
internal static class FlowReport
{
    // Playwright appends its action log to a failure message after this line; the first line naming what it waited
    // for tells which element the flow was stuck on.
    private const string CallLogMarker = "Call log:";
    private const string WaitingForPrefix = "- waiting for ";

    private const string NoDetail = "(no failure detail)";

    private const string NoSessionAttached = "No session is attached to this assertion, so nothing was verified";

    /// <summary>A pass/fail count, the first failure's reason, the sessions that ran no flow, and the flows that passed.</summary>
    public static string Message(SessionResults results)
    {
        if (results.SessionNames.Count == 0) return $"{NoSessionAttached}.";

        var passed = results.Outcomes.Where(outcome => outcome.Passed).Select(outcome => outcome.FlowName).ToList();
        if (results.Passed) return $"All {passed.Count} Playwright flow(s) passed: {string.Join(", ", passed)}.";

        string?[] problems = [FailureHeadline(results), NothingVerifiedHeadline(results.UnverifiedSessions)];
        var passedNames = passed.Count > 0 ? string.Join(", ", passed) : "none";
        return $"{string.Join(". ", problems.OfType<string>())}. Passed: {passedNames}.";
    }

    /// <summary>A PASS/FAIL line per flow in run order, then each failure's full message and call log.</summary>
    public static string Trace(SessionResults results)
    {
        var outcomes = results.Outcomes;
        var failed = outcomes.Where(outcome => !outcome.Passed).ToList();
        var trace = new StringBuilder(outcomes.Count == 0
            ? "Playwright journey — no flow outcomes were recorded."
            : $"Playwright journey — {outcomes.Count - failed.Count} of {outcomes.Count} flow(s) passed" +
              (failed.Count > 0 ? $", {failed.Count} failed." : "."));

        foreach (var outcome in outcomes)
            trace.AppendLine().Append($"  [{(outcome.Passed ? "PASS" : "FAIL")}]  {outcome.FlowName}");

        foreach (var outcome in failed)
            AppendSection(trace, $"{outcome.FlowName} failed", FailureDetail(outcome));

        // A failing flow also fails its session with the same message; show only the session failures that add something.
        var flowMessages = failed.Select(outcome => outcome.FailureMessage).ToHashSet();
        foreach (var failure in results.SessionFailures.Where(failure => !flowMessages.Contains(failure.Reason.Message)))
            AppendSection(trace, $"session failure: {failure.Name}", failure.Reason.Message);

        if (results.SessionNames.Count == 0)
            AppendSection(trace, "nothing verified",
                $"{NoSessionAttached}: its SessionNames match no session that ran.");
        else if (results.UnverifiedSessions.Count > 0)
            AppendSection(trace, "nothing verified", LikelyCauses(results.UnverifiedSessions));

        return trace.ToString();
    }

    private static string? FailureHeadline(SessionResults results)
    {
        var outcomes = results.Outcomes;
        var failed = outcomes.Where(outcome => !outcome.Passed).ToList();
        if (failed.Count == 0)
            return results.SessionFailures.Count == 0
                ? null
                : $"{results.SessionFailures.Count} session failure(s) with no completed flow — " +
                  Summarize(results.SessionFailures[0].Reason.Message);

        var first = failed[0];
        return failed.Count == 1
            ? $"{first.FlowName} failed ({outcomes.Count - failed.Count}/{outcomes.Count} flows passed): {Describe(first)}"
            : $"{failed.Count} of {outcomes.Count} flows failed " +
              $"({string.Join(", ", failed.Select(outcome => outcome.FlowName))}); first '{first.FlowName}': {Describe(first)}";
    }

    // e.g. "Timeout 3000ms exceeded (waiting for GetByRole(...)) on http://app/orders".
    private static string Describe(PlaywrightFlowOutcome failure)
    {
        var description = Summarize(failure.FailureMessage);
        if (WaitingFor(failure.FailureMessage) is { } element) description += $" ({element})";
        if (!string.IsNullOrEmpty(failure.FailureUrl)) description += $" on {failure.FailureUrl}";
        return description;
    }

    private static string? FailureDetail(PlaywrightFlowOutcome failure) =>
        failure.FailureUrl is null ? failure.FailureMessage : $"Page: {failure.FailureUrl}\n{failure.FailureMessage ?? NoDetail}";

    private static string? WaitingFor(string? failureMessage) =>
        failureMessage?.Split(CallLogMarker, 2).ElementAtOrDefault(1)?
            .Split('\n', StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.StartsWith(WaitingForPrefix, StringComparison.Ordinal))?[2..];

    private static string? NothingVerifiedHeadline(IReadOnlyList<string> unverifiedSessions) =>
        unverifiedSessions.Count == 0
            ? null
            : $"No Playwright flow ran in session(s) {string.Join(", ", unverifiedSessions)}, so nothing was verified there";

    // The assertion cannot tell these apart, so it lists them all.
    private static string LikelyCauses(IReadOnlyList<string> unverifiedSessions) =>
        $"No flow outcome and no failure was recorded in session(s) {string.Join(", ", unverifiedSessions)}. Likely causes:\n" +
        "  - the probe's Flows and SetupFlows are empty, or misspelled (e.g. 'Flow:'; the probe warns about unknown keys);\n" +
        "  - the session has no PlaywrightFlowProbe;\n" +
        "  - the probe runs in another session than the ones this assertion's SessionNames select.";

    // The failure message on one line, without the call log (the trace keeps it) or a closing period (the message
    // adds its own).
    private static string Summarize(string? failureMessage)
    {
        if (string.IsNullOrWhiteSpace(failureMessage)) return NoDetail;
        var beforeCallLog = failureMessage.Split(CallLogMarker, 2)[0];
        var lines = beforeCallLog.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(' ', lines).TrimEnd('.');
    }

    private static void AppendSection(StringBuilder trace, string title, string? detail) =>
        trace.AppendLine().AppendLine().AppendLine($"---- {title} ----")
            .Append(string.IsNullOrWhiteSpace(detail) ? NoDetail : detail.TrimEnd());
}
