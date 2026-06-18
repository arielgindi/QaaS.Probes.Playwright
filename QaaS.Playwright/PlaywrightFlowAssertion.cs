using System.Collections.Immutable;
using System.Text;
using Microsoft.Extensions.Logging;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using AssertionOutcome = QaaS.Framework.SDK.Hooks.Assertion.AssertionStatus;

namespace QaaS.Playwright;

/// <summary>
/// This assertion has no configuration; it reports on whichever sessions it is attached to.
/// </summary>
public sealed record PlaywrightFlowAssertionConfiguration;

/// <summary>
/// The assertion half of the Playwright plugin — it pairs with the PlaywrightFlowProbe through the shared
/// <see cref="PlaywrightFlowResults"/> contract.
///
/// For each attached session it reads the per-flow outcomes the probe recorded and combines them with the
/// session's own recorded failures, then produces one granular result for the report: it names the flow that
/// failed, lists the flows that passed, attaches each failure screenshot to Allure, and fails if either a flow
/// failed or the session recorded a failure — so a problem can never be hidden behind passing flow outcomes.
/// </summary>
public sealed class PlaywrightFlowAssertion : BaseAssertion<PlaywrightFlowAssertionConfiguration>
{
    // Playwright embeds its action trace after this literal in a failure message. We split on it for the one-line
    // headline; it is a named constant to make the coupling to Playwright's (English) message format explicit.
    private const string PlaywrightCallLogMarker = "Call log:";

    private const string NoFailureDetail = "(no failure detail)";

    /// <inheritdoc />
    public override bool Assert(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
    {
        // The runner invokes Assert exactly once per instance (one instance per uniquely-named assertion, a fresh
        // scope per execution), so no inter-call state reset is needed.
        var flowOutcomes = sessionDataList
            .SelectMany(session => PlaywrightFlowResults.Read(Context, session.Name))
            // Also fold in any outcomes recorded without a session scope, so a missing scope degrades gracefully
            // instead of silently reporting "all passed".
            .Concat(PlaywrightFlowResults.Read(Context, PlaywrightFlowResults.UnscopedSessionName))
            .ToList();
        var sessionFailures = sessionDataList
            .SelectMany(session => session.SessionFailures)
            .ToList();

        var passedFlows = flowOutcomes.Where(outcome => outcome.Passed).ToList();
        var failedFlows = flowOutcomes.Where(outcome => !outcome.Passed).ToList();
        var passed = failedFlows.Count == 0 && sessionFailures.Count == 0;

        AssertionMessage = BuildMessage(passedFlows, failedFlows, sessionFailures);
        if (!passed)
        {
            AssertionTrace = BuildTrace(flowOutcomes, sessionFailures);
            AttachFailureScreenshots(failedFlows);
        }

        AssertionStatus = passed ? AssertionOutcome.Passed : AssertionOutcome.Failed;
        Context.Logger.LogInformation("PlaywrightFlowAssertion: passed={Passed} — {Message}", passed, AssertionMessage);
        return passed;
    }

    // A one-line headline for the report: a pass/fail count plus the first failure's reason. The verbose Playwright
    // call log is kept out of it and lives in the trace (BuildTrace), so the top of the report reads at a glance.
    private static string BuildMessage(
        IReadOnlyList<PlaywrightFlowOutcome> passedFlows,
        IReadOnlyList<PlaywrightFlowOutcome> failedFlows,
        IReadOnlyCollection<ActionFailure> sessionFailures)
    {
        var total = passedFlows.Count + failedFlows.Count;
        var passedNames = passedFlows.Count > 0
            ? string.Join(", ", passedFlows.Select(outcome => outcome.FlowName))
            : "none";

        if (failedFlows.Count == 0 && sessionFailures.Count == 0)
            return total > 0
                ? $"All {total} Playwright flow(s) passed: {passedNames}."
                : "All Playwright flow steps passed.";

        string headline;
        if (failedFlows.Count == 0)
        {
            // No flow recorded an outcome, yet the probe/session still failed (e.g. it crashed before the first flow).
            headline = $"{sessionFailures.Count} session failure(s) with no completed flow — " +
                       Summarize(sessionFailures.First().Reason.Message);
        }
        else
        {
            var first = failedFlows[0];
            var reason = Summarize(first.FailureMessage);
            headline = failedFlows.Count == 1
                ? $"{first.FlowName} failed ({passedFlows.Count}/{total} flows passed): {reason}"
                : $"{failedFlows.Count} of {total} flows failed " +
                  $"({string.Join(", ", failedFlows.Select(outcome => outcome.FlowName))}); first '{first.FlowName}': {reason}";
        }

        return $"{headline}. Passed: {passedNames}.";
    }

    // Collapse Playwright's multi-line failure (assertion message + call log) into a single readable sentence: drop
    // everything from the call-log marker on, and flatten the remaining lines. The full detail is preserved in the trace.
    private static string Summarize(string? failureMessage)
    {
        if (string.IsNullOrWhiteSpace(failureMessage)) return NoFailureDetail;
        var beforeCallLog = failureMessage.Split(PlaywrightCallLogMarker, 2, StringSplitOptions.None)[0];
        return string.Join(' ', beforeCallLog.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    // A readable, structured breakdown for the report's trace: a one-line summary, a PASS/FAIL checklist of every flow
    // in execution order, then a delimited section per failure carrying the full Playwright message and call log.
    private static string BuildTrace(
        IReadOnlyList<PlaywrightFlowOutcome> flowOutcomes,
        IReadOnlyList<ActionFailure> sessionFailures)
    {
        var passedCount = flowOutcomes.Count(outcome => outcome.Passed);
        var failed = flowOutcomes.Where(outcome => !outcome.Passed).ToList();

        var trace = new StringBuilder();
        trace.Append(flowOutcomes.Count == 0
            ? "Playwright journey — no flow outcomes were recorded."
            : $"Playwright journey — {passedCount} of {flowOutcomes.Count} flow(s) passed" +
              (failed.Count > 0 ? $", {failed.Count} failed." : "."));

        foreach (var outcome in flowOutcomes)
            trace.AppendLine().Append($"  [{(outcome.Passed ? "PASS" : "FAIL")}]  {outcome.FlowName}");

        foreach (var outcome in failed)
            AppendDetail(trace, $"{outcome.FlowName} failed", outcome.FailureMessage);

        // Skip session failures that merely re-surface a flow failure the probe re-threw — otherwise the same error
        // is reported twice. A session failure with a distinct message (a real probe-level crash) is still shown.
        var flowFailureMessages = failed
            .Where(outcome => outcome.FailureMessage is not null)
            .Select(outcome => outcome.FailureMessage!)
            .ToHashSet();
        foreach (var failure in sessionFailures.Where(failure => !flowFailureMessages.Contains(failure.Reason.Message)))
            AppendDetail(trace, $"session failure: {failure.Name}", failure.Reason.Message);

        return trace.ToString();
    }

    private static void AppendDetail(StringBuilder trace, string title, string? detail) =>
        trace.AppendLine().AppendLine().AppendLine($"---- {title} ----")
            .Append(string.IsNullOrWhiteSpace(detail) ? NoFailureDetail : detail.TrimEnd());

    private void AttachFailureScreenshots(IEnumerable<PlaywrightFlowOutcome> failedFlows)
    {
        foreach (var failure in failedFlows.Where(outcome => outcome.FailureScreenshot is not null))
        {
            AssertionAttachments.Add(new AssertionAttachment
            {
                Path = $"{Sanitize(failure.FlowName)}-failure.png",
                Data = failure.FailureScreenshot,
                // Leave SerializationType null so the reporter writes the PNG bytes verbatim. SerializationType.Binary
                // would run the bytes through BinaryFormatter, framing the PNG so no image viewer could open it.
                SerializationType = null,
            });
        }
    }

    // Keep only characters that are safe in a file name, so an unusual flow name cannot escape the
    // attachments directory or collide unexpectedly.
    private static string Sanitize(string flowName) =>
        string.Concat(flowName.Select(character => char.IsLetterOrDigit(character) ? character : '_'));
}
