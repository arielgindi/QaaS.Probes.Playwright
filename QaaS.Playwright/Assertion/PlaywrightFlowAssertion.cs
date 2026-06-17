using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.Serialization;
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

        var passedFlows = flowOutcomes.Where(outcome => outcome.Passed).Select(outcome => outcome.FlowName).ToList();
        var failedFlows = flowOutcomes.Where(outcome => !outcome.Passed).ToList();
        var passed = failedFlows.Count == 0 && sessionFailures.Count == 0;

        AssertionMessage = BuildMessage(passed, passedFlows, failedFlows, sessionFailures);
        if (!passed)
        {
            AssertionTrace = BuildTrace(flowOutcomes, sessionFailures);
            AttachFailureScreenshots(failedFlows);
        }

        AssertionStatus = passed ? AssertionOutcome.Passed : AssertionOutcome.Failed;
        Context.Logger.LogInformation("PlaywrightFlowAssertion: passed={Passed} — {Message}", passed, AssertionMessage);
        return passed;
    }

    private static string BuildMessage(
        bool passed,
        IReadOnlyCollection<string> passedFlows,
        IReadOnlyList<PlaywrightFlowOutcome> failedFlows,
        IReadOnlyCollection<ActionFailure> sessionFailures)
    {
        if (passed)
            return passedFlows.Count > 0
                ? $"All {passedFlows.Count} Playwright flow(s) passed: {string.Join(", ", passedFlows)}."
                : "All Playwright flow steps passed.";

        var reasons = new List<string>();
        if (failedFlows.Count > 0)
            reasons.Add($"Flow '{failedFlows[0].FlowName}' failed: {failedFlows[0].FailureMessage}");
        if (sessionFailures.Count > 0)
            reasons.Add($"{sessionFailures.Count} session failure(s)");

        var passedSummary = passedFlows.Count > 0 ? string.Join(", ", passedFlows) : "none";
        return $"{string.Join("; ", reasons)} — passed: {passedSummary}.";
    }

    private static string BuildTrace(
        IReadOnlyList<PlaywrightFlowOutcome> flowOutcomes,
        IReadOnlyList<ActionFailure> sessionFailures)
    {
        var lines = flowOutcomes
            .Select(outcome => outcome.Passed
                ? $"passed  {outcome.FlowName}"
                : $"FAILED  {outcome.FlowName}: {outcome.FailureMessage}")
            .Concat(sessionFailures.Select(failure => $"FAILED  [session:{failure.Name}] {failure.Reason.Message}"));
        return string.Join(Environment.NewLine, lines);
    }

    private void AttachFailureScreenshots(IEnumerable<PlaywrightFlowOutcome> failedFlows)
    {
        foreach (var failure in failedFlows.Where(outcome => outcome.FailureScreenshot is not null))
        {
            AssertionAttachments.Add(new AssertionAttachment
            {
                Path = $"{Sanitize(failure.FlowName)}-failure.png",
                Data = failure.FailureScreenshot,
                SerializationType = SerializationType.Binary,
            });
        }
    }

    // Keep only characters that are safe in a file name, so an unusual flow name cannot escape the
    // attachments directory or collide unexpectedly.
    private static string Sanitize(string flowName) =>
        string.Concat(flowName.Select(character => char.IsLetterOrDigit(character) ? character : '_'));
}
