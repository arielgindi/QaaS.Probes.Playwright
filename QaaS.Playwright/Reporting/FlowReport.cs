using System.Text;

namespace QaaS.Playwright.Reporting;

/// <summary>
/// The report text of <see cref="PlaywrightFlowAssertion"/>: a one-line message and a detailed trace. Lines end in
/// <c>\n</c> on every OS, as the failure messages it quotes do.
/// </summary>
internal static class FlowReport
{
    // Playwright appends its action log to a failure message after this line. Its first "waiting for" line names the
    // element the flow was stuck on.
    private const string CallLogMarker = "Call log:";
    private const string WaitingForPrefix = "- waiting for ";

    private const string NoDetail = "(no failure detail)";
    private const string NoSessionAttached = "No session is attached to this assertion, so nothing was verified";

    /// <summary>What failed and why, which sessions ran no flow, which flows passed, and how many warnings came.</summary>
    public static string Message(SessionResults results)
    {
        var warnings = results.Warnings.Count > 0 ? $" {results.Warnings.Count} warning(s), see the trace." : "";
        if (results.SessionNames.Count == 0) return $"{NoSessionAttached}.{warnings}";

        var passed = results.Outcomes.Where(outcome => outcome.Passed).Select(outcome => outcome.FlowName).ToList();
        if (results.Passed) return $"All {passed.Count} Playwright flow(s) passed: {string.Join(", ", passed)}.{warnings}";

        string?[] problems = [FailureHeadline(results), NothingVerifiedHeadline(results.UnverifiedSessions)];
        var passedNames = passed.Count > 0 ? string.Join(", ", passed) : "none";
        return $"{string.Join(". ", problems.OfType<string>())}. Passed: {passedNames}.{warnings}";
    }

    /// <summary>
    /// A PASS/FAIL line per flow in run order, then each failure in full, with its call log and stack, and the probes'
    /// warnings. With several sessions or probes, each flow is named with where it ran, e.g. <c>Checkout/Buyer: Pay</c>.
    /// </summary>
    public static string Trace(SessionResults results)
    {
        var entries = results.SessionOutcomes;
        var failed = entries.Where(entry => !entry.Outcome.Passed).ToList();
        var trace = new StringBuilder(entries.Count == 0
            ? "Playwright journey — no flow outcomes were recorded."
            : $"Playwright journey — {entries.Count - failed.Count} of {entries.Count} flow(s) passed" +
              (failed.Count > 0 ? $", {failed.Count} failed." : "."));

        var scoped = entries.Select(entry => (entry.SessionName, entry.Outcome.ProbeName)).Distinct().Count() > 1;
        string NameOf((string SessionName, PlaywrightFlowOutcome Outcome) entry) =>
            !scoped ? entry.Outcome.FlowName
            : entry.Outcome.ProbeName is null ? $"{entry.SessionName}: {entry.Outcome.FlowName}"
            : $"{entry.SessionName}/{entry.Outcome.ProbeName}: {entry.Outcome.FlowName}";

        foreach (var entry in entries)
            trace.Append($"\n  [{(entry.Outcome.Passed ? "PASS" : "FAIL")}]  {NameOf(entry)}");

        foreach (var entry in failed)
            AppendSection(trace, $"{NameOf(entry)} failed", FailureDetail(entry.Outcome));

        // A failing flow also fails its session with its exception; show only the session failures that add something.
        var flowFailures = failed.Select(entry => $"{entry.Outcome.FailureMessage}\n{entry.Outcome.FailureDetail}").ToList();
        var sessionFailures = results.SessionFailures
            .Where(failure => !flowFailures.Any(flowFailure => flowFailure.Contains(failure.Reason.Message)));
        foreach (var failure in sessionFailures)
            AppendSection(trace, $"session failure: {failure.Name}", failure.Reason.Message);

        if (results.SessionNames.Count == 0)
            AppendSection(trace, "nothing verified", $"{NoSessionAttached}: its SessionNames match no session that ran.");
        else if (results.UnverifiedSessions.Count > 0)
            AppendSection(trace, "nothing verified", LikelyCauses(results.UnverifiedSessions));

        if (results.Warnings.Count > 0)
            trace.Append("\n\nWarnings:")
                .AppendJoin("", results.Warnings.Select(entry => $"\n  - {entry.SessionName}: {entry.Warning}"));
        return trace.ToString();
    }

    private static string? FailureHeadline(SessionResults results)
    {
        var outcomes = results.Outcomes;
        var failed = outcomes.Where(outcome => !outcome.Passed).ToList();
        var sessionFailures = results.SessionFailures;
        if (failed.Count == 0)
            return sessionFailures.Count == 0
                ? null
                : $"{sessionFailures.Count} session failure(s): {Summarize(sessionFailures[0].Reason.Message)}";

        var first = failed[0];
        return failed.Count == 1
            ? $"{first.FlowName} failed ({outcomes.Count - 1}/{outcomes.Count} flows passed): {Describe(first)}"
            : $"{failed.Count} of {outcomes.Count} flows failed " +
              $"({string.Join(", ", failed.Select(outcome => outcome.FlowName))}); first '{first.FlowName}': {Describe(first)}";
    }

    /// <summary>
    /// The failure on one line, as the assertion's message tells it: e.g. "Timeout 3000ms exceeded (waiting for
    /// GetByRole(...)) on http://app/cart, with pop-up http://app/help open". The recorder's test run tells it so too.
    /// </summary>
    internal static string Describe(PlaywrightFlowOutcome failure)
    {
        var description = Summarize(failure.FailureMessage);
        if (WaitingFor(failure.FailureMessage) is { } element) description += $" ({element})";
        if (!string.IsNullOrEmpty(failure.FailureUrl)) description += $" on {failure.FailureUrl}";
        var popups = failure.FailurePopups ?? [];
        if (popups.Count > 0)
            description += $", with {(popups.Count == 1 ? "pop-up" : "pop-ups")} " +
                           $"{string.Join(", ", popups.Select(popup => popup.Url))} open";
        return description;
    }

    /// <summary>The element the flow was stuck on, from the first "waiting for" line of Playwright's call log.</summary>
    internal static string? WaitingFor(string? failureMessage) =>
        failureMessage?.Split(CallLogMarker, 2).ElementAtOrDefault(1)?
            .Split('\n', StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.StartsWith(WaitingForPrefix, StringComparison.Ordinal))?[2..];

    /// <summary>Playwright's call log in the failure message: what it did and waited for, a line each; null when none.</summary>
    internal static string? CallLog(string? failureMessage) =>
        failureMessage?.Split(CallLogMarker, 2).ElementAtOrDefault(1)?.Trim('\n', '\r', ' ') is { Length: > 0 } log ? log : null;

    /// <summary>The failure message on one line, without the call log or a closing period (the message adds its own).</summary>
    internal static string Summarize(string? failureMessage)
    {
        if (string.IsNullOrWhiteSpace(failureMessage)) return NoDetail;
        var beforeCallLog = failureMessage.Split(CallLogMarker, 2)[0];
        var lines = beforeCallLog.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(' ', lines).TrimEnd('.');
    }

    private static string? NothingVerifiedHeadline(IReadOnlyList<string> unverifiedSessions) =>
        unverifiedSessions.Count == 0
            ? null
            : $"No Playwright flow ran in session(s) {string.Join(", ", unverifiedSessions)}, so nothing was verified there";

    // The flow's page, each pop-up it left open, numbered as their screenshots are, and the whole failure.
    private static string? FailureDetail(PlaywrightFlowOutcome failure)
    {
        var detail = failure.FailureDetail ?? failure.FailureMessage;
        if (failure.FailureUrl is null) return detail;
        var popups = (failure.FailurePopups ?? []).Select((popup, index) => $"Pop-up {index + 1}: {popup.Url}\n");
        return $"Page: {failure.FailureUrl}\n{string.Concat(popups)}{detail ?? NoDetail}";
    }

    // The assertion cannot tell these apart, so it lists them all.
    private static string LikelyCauses(IReadOnlyList<string> unverifiedSessions) =>
        $"Session(s) {string.Join(", ", unverifiedSessions)} recorded no flow outcome and no failure. Likely causes:\n" +
        "  - the session has no PlaywrightFlowProbe;\n" +
        "  - the probe runs in another session than the ones this assertion's SessionNames select;\n" +
        "  - the probes ran in an 'act' command and this assertion in a later 'assert': flow outcomes are kept only in\n" +
        "    memory, so run both in one 'run' command.";

    private static void AppendSection(StringBuilder trace, string title, string? detail) =>
        trace.Append($"\n\n---- {title} ----\n")
            .Append(string.IsNullOrWhiteSpace(detail) ? NoDetail : detail.TrimEnd());
}
