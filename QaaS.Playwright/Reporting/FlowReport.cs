using System.Text;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Playwright.Reporting;

/// <summary>The report text of <see cref="PlaywrightFlowAssertion"/>: a one-line message and a detailed trace.</summary>
internal static class FlowReport
{
    // Playwright appends its action log to a failure message after this line.
    private const string CallLogMarker = "Call log:";

    private const string NoDetail = "(no failure detail)";

    /// <summary>A pass/fail count, the first failure's reason, and the flows that passed.</summary>
    public static string Message(
        IReadOnlyList<PlaywrightFlowOutcome> outcomes, IReadOnlyCollection<ActionFailure> sessionFailures)
    {
        var passed = outcomes.Where(outcome => outcome.Passed).ToList();
        var failed = outcomes.Where(outcome => !outcome.Passed).ToList();
        var passedNames = passed.Count > 0 ? string.Join(", ", passed.Select(outcome => outcome.FlowName)) : "none";

        if (failed.Count == 0 && sessionFailures.Count == 0)
            return outcomes.Count > 0
                ? $"All {outcomes.Count} Playwright flow(s) passed: {passedNames}."
                : "All Playwright flow steps passed.";

        string headline;
        if (failed.Count == 0)
        {
            // The session failed before any flow finished, e.g. the probe could not reach the browser.
            headline = $"{sessionFailures.Count} session failure(s) with no completed flow — " +
                       Summarize(sessionFailures.First().Reason.Message);
        }
        else
        {
            var first = failed[0];
            headline = failed.Count == 1
                ? $"{first.FlowName} failed ({passed.Count}/{outcomes.Count} flows passed): {Summarize(first.FailureMessage)}"
                : $"{failed.Count} of {outcomes.Count} flows failed " +
                  $"({string.Join(", ", failed.Select(outcome => outcome.FlowName))}); " +
                  $"first '{first.FlowName}': {Summarize(first.FailureMessage)}";
        }

        return $"{headline}. Passed: {passedNames}.";
    }

    /// <summary>A PASS/FAIL line per flow in run order, then each failure's full message and call log.</summary>
    public static string Trace(
        IReadOnlyList<PlaywrightFlowOutcome> outcomes, IReadOnlyCollection<ActionFailure> sessionFailures)
    {
        var failed = outcomes.Where(outcome => !outcome.Passed).ToList();
        var trace = new StringBuilder(outcomes.Count == 0
            ? "Playwright journey — no flow outcomes were recorded."
            : $"Playwright journey — {outcomes.Count - failed.Count} of {outcomes.Count} flow(s) passed" +
              (failed.Count > 0 ? $", {failed.Count} failed." : "."));

        foreach (var outcome in outcomes)
            trace.AppendLine().Append($"  [{(outcome.Passed ? "PASS" : "FAIL")}]  {outcome.FlowName}");

        foreach (var outcome in failed)
            AppendSection(trace, $"{outcome.FlowName} failed", outcome.FailureMessage);

        // A failing flow also fails its session with the same message; show only the session failures that add something.
        var flowMessages = failed.Select(outcome => outcome.FailureMessage).ToHashSet();
        foreach (var failure in sessionFailures.Where(failure => !flowMessages.Contains(failure.Reason.Message)))
            AppendSection(trace, $"session failure: {failure.Name}", failure.Reason.Message);

        return trace.ToString();
    }

    // The failure message on one line, without the call log (the trace keeps it).
    private static string Summarize(string? failureMessage)
    {
        if (string.IsNullOrWhiteSpace(failureMessage)) return NoDetail;
        var beforeCallLog = failureMessage.Split(CallLogMarker, 2)[0];
        return string.Join(' ', beforeCallLog.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static void AppendSection(StringBuilder trace, string title, string? detail) =>
        trace.AppendLine().AppendLine().AppendLine($"---- {title} ----")
            .Append(string.IsNullOrWhiteSpace(detail) ? NoDetail : detail.TrimEnd());
}
