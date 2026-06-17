using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.Serialization;
using QaaS.Probes.Playwright;

namespace QaaS.Assertions.Playwright;

/// <summary>
/// This assertion has no configuration; it reports on whichever sessions it is attached to.
/// </summary>
public sealed record PlaywrightFlowAssertionConfiguration;

/// <summary>
/// The assertion half of the Playwright plugin — it pairs with <see cref="PlaywrightFlowProbe"/>.
///
/// The probe runs the configured flows and records each one's <see cref="PlaywrightFlowOutcome"/> via
/// <see cref="PlaywrightFlowResults"/>. This assertion reads that per-flow breakdown and turns it into a
/// single, granular pass/fail result for the report: it names the flow that failed, lists the flows that
/// passed, and attaches a screenshot of any failure to Allure. If no per-flow results are present (for
/// example a non-Playwright session) it falls back to the session's recorded failures.
/// </summary>
public sealed class PlaywrightFlowAssertion : BaseAssertion<PlaywrightFlowAssertionConfiguration>
{
    /// <inheritdoc />
    public override bool Assert(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
    {
        var flowOutcomes = PlaywrightFlowResults.Read(Context);

        var passed = flowOutcomes.Count > 0
            ? ReportFlowOutcomes(flowOutcomes)
            : ReportSessionFailures(sessionDataList);

        Context.Logger.LogInformation("PlaywrightFlowAssertion: passed={Passed} — {Message}", passed, AssertionMessage);
        return passed;
    }

    private bool ReportFlowOutcomes(IReadOnlyList<PlaywrightFlowOutcome> outcomes)
    {
        var passedFlows = outcomes.Where(outcome => outcome.Passed).Select(outcome => outcome.FlowName).ToList();
        var failedFlow = outcomes.FirstOrDefault(outcome => !outcome.Passed);

        if (failedFlow is null)
        {
            AssertionMessage = $"All {passedFlows.Count} Playwright flow(s) passed: {string.Join(", ", passedFlows)}.";
            return true;
        }

        var passedSummary = passedFlows.Count == 0 ? "none" : string.Join(", ", passedFlows);
        AssertionMessage = $"Flow '{failedFlow.FlowName}' failed: {failedFlow.FailureMessage} — passed: {passedSummary}.";
        AssertionTrace = string.Join(
            Environment.NewLine,
            outcomes.Select(outcome => outcome.Passed
                ? $"passed  {outcome.FlowName}"
                : $"FAILED  {outcome.FlowName}: {outcome.FailureMessage}"));

        AttachFailureScreenshots(outcomes);
        return false;
    }

    private void AttachFailureScreenshots(IReadOnlyList<PlaywrightFlowOutcome> outcomes)
    {
        foreach (var failure in outcomes.Where(outcome => !outcome.Passed && outcome.FailureScreenshot is not null))
        {
            AssertionAttachments.Add(new AssertionAttachment
            {
                Path = $"{failure.FlowName}-failure.png",
                Data = failure.FailureScreenshot,
                SerializationType = SerializationType.Binary,
            });
        }
    }

    private bool ReportSessionFailures(IImmutableList<SessionData> sessionDataList)
    {
        var failures = sessionDataList.SelectMany(session => session.SessionFailures).ToList();

        if (failures.Count == 0)
        {
            AssertionMessage = "All Playwright flow steps passed.";
            return true;
        }

        AssertionMessage = $"{failures.Count} Playwright flow step(s) failed.";
        AssertionTrace = string.Join(Environment.NewLine, failures.Select(failure => $"[{failure.Name}] {failure.Reason.Message}"));
        return false;
    }
}
