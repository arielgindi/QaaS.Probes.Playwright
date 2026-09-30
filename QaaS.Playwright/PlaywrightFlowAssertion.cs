using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
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
/// why, with each failure's screenshot attached, and every warning the probes logged. It fails when a flow failed, a
/// session recorded a failure, or nothing was verified: no session is attached, or an attached session ran no flow.
/// </summary>
public sealed class PlaywrightFlowAssertion : BaseAssertion<PlaywrightFlowAssertionConfiguration>
{
    public override bool Assert(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
    {
        var results = SessionResults.Collect(Context, sessionDataList);

        AssertionMessage = FlowReport.Message(results);
        AssertionTrace = FlowReport.Trace(results);
        if (!results.Passed) AttachScreenshots(results);

        AssertionStatus = results.Passed ? AssertionOutcome.Passed : AssertionOutcome.Failed;
        Context.Logger.LogInformation(
            "PlaywrightFlowAssertion: passed={Passed} — {Message}", results.Passed, AssertionMessage);
        return results.Passed;
    }

    // Named after the session, the probe and the flow, and never twice: parallel probes of one session often run the
    // same flow, and the Allure reporter aborts the whole run when two attachments share a path, ignoring case, or when
    // a path is too long for the file system.
    private void AttachScreenshots(SessionResults results)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var screenshots = results.SessionOutcomes.Where(entry => entry.Outcome.FailureScreenshot is not null);
        foreach (var (sessionName, failure) in screenshots)
        {
            var name = FileNameOf([sessionName, failure.ProbeName, failure.FlowName, "failure"]);
            var path = $"{name}.png";
            for (var copy = 2; !paths.Add(path); copy++) path = $"{name}-{copy}.png";

            // No SerializationType: the reporter must write the PNG bytes as they are.
            AssertionAttachments.Add(new AssertionAttachment { Path = path, Data = failure.FailureScreenshot });
        }
    }

    // ASCII letters and digits, each part cut to 30 characters, so the name stays near 100 bytes whatever the names;
    // a short hash of the whole names then keeps apart runs whose names differ only further on.
    private static string FileNameOf(string?[] parts)
    {
        const int maxPartLength = 30;
        var names = parts.OfType<string>().ToList();
        var fileName = string.Join('-', names.Select(name => string.Concat(name.Take(maxPartLength)
            .Select(character => char.IsAsciiLetterOrDigit(character) ? character : '_'))));
        if (names.All(name => name.Length <= maxPartLength)) return fileName;

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', names)));
        return $"{fileName}-{Convert.ToHexStringLower(hash)[..8]}";
    }
}
