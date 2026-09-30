using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Playwright.Reporting;
using AssertionOutcome = QaaS.Framework.SDK.Hooks.Assertion.AssertionStatus;

namespace QaaS.Playwright;

/// <summary>The assertion has no settings; it reports on the sessions it is attached to.</summary>
public sealed record PlaywrightFlowAssertionConfiguration;

/// <summary>
/// Reports the flows <see cref="PlaywrightFlowProbe"/> ran in the attached sessions: which passed, which failed and
/// why, with each failure's screenshot attached. It fails when a flow failed, a session recorded a failure, or
/// nothing was verified: no session is attached, or an attached session ran no flow.
/// </summary>
public sealed class PlaywrightFlowAssertion : BaseAssertion<PlaywrightFlowAssertionConfiguration>
{
    public override bool Assert(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
    {
        var results = SessionResults.Collect(Context, sessionDataList);

        AssertionMessage = FlowReport.Message(results);
        if (!results.Passed)
        {
            AssertionTrace = FlowReport.Trace(results);
            AttachScreenshots(results.Outcomes);
        }

        AssertionStatus = results.Passed ? AssertionOutcome.Passed : AssertionOutcome.Failed;
        Context.Logger.LogInformation("PlaywrightFlowAssertion: passed={Passed} — {Message}", results.Passed, AssertionMessage);
        return results.Passed;
    }

    private void AttachScreenshots(IEnumerable<PlaywrightFlowOutcome> outcomes)
    {
        foreach (var failure in outcomes.Where(outcome => outcome.FailureScreenshot is not null))
        {
            // No SerializationType: the reporter must write the PNG bytes as they are.
            AssertionAttachments.Add(new AssertionAttachment
            {
                Path = $"{FileNameOf(failure.FlowName)}-failure.png",
                Data = failure.FailureScreenshot,
            });
        }
    }

    private static string FileNameOf(string flowName) =>
        string.Concat(flowName.Select(character => char.IsLetterOrDigit(character) ? character : '_'));
}
