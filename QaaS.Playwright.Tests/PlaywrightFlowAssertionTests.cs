using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using AssertionOutcome = QaaS.Framework.SDK.Hooks.Assertion.AssertionStatus;

namespace QaaS.Playwright.Tests;

[TestFixture]
public class PlaywrightFlowAssertionTests
{
    private static (PlaywrightFlowAssertion Assertion, Context Context) NewAssertion()
    {
        var context = new Context { Logger = NullLogger.Instance };
        return (new PlaywrightFlowAssertion { Context = context }, context);
    }

    private static IImmutableList<SessionData> Sessions(params SessionData[] sessions) => sessions.ToImmutableList();

    private static IImmutableList<DataSource> NoDataSources => ImmutableList<DataSource>.Empty;

    [Test]
    public void Assert_AllFlowsPassed_ReportsPassWithBothNames()
    {
        var (assertion, context) = NewAssertion();
        PlaywrightFlowResults.Record(context, "Journey", new PlaywrightFlowOutcome("SignIn", Passed: true));
        PlaywrightFlowResults.Record(context, "Journey", new PlaywrightFlowOutcome("Todo", Passed: true));

        var passed = assertion.Assert(Sessions(new SessionData { Name = "Journey" }), NoDataSources);

        Assert.That(passed, Is.True);
        Assert.That(assertion.AssertionStatus, Is.EqualTo(AssertionOutcome.Passed));
        Assert.That(assertion.AssertionMessage, Does.Contain("SignIn").And.Contains("Todo"));
        Assert.That(assertion.AssertionAttachments, Is.Empty);
    }

    [Test]
    public void Assert_FlowFailed_NamesTheFlowAndAttachesItsScreenshot()
    {
        var (assertion, context) = NewAssertion();
        PlaywrightFlowResults.Record(context, "Journey", new PlaywrightFlowOutcome("SignIn", Passed: true));
        PlaywrightFlowResults.Record(context, "Journey",
            new PlaywrightFlowOutcome("Todo", Passed: false, "count mismatch", [1, 2, 3]));

        var passed = assertion.Assert(Sessions(new SessionData { Name = "Journey" }), NoDataSources);

        Assert.That(passed, Is.False);
        Assert.That(assertion.AssertionStatus, Is.EqualTo(AssertionOutcome.Failed));
        Assert.That(assertion.AssertionMessage, Does.Contain("Todo").And.Contains("count mismatch"));
        Assert.That(assertion.AssertionMessage, Does.Contain("SignIn"), "passed flows should still be listed");
        Assert.That(assertion.AssertionAttachments, Has.Count.EqualTo(1));
        Assert.That(assertion.AssertionAttachments[0].Path, Does.Contain("Todo"));
    }

    [Test]
    public void Assert_AllFlowsPassedButSessionFailed_StillReportsFail()
    {
        var (assertion, context) = NewAssertion();
        PlaywrightFlowResults.Record(context, "Journey", new PlaywrightFlowOutcome("SignIn", Passed: true));
        var session = new SessionData
        {
            Name = "Journey",
            SessionFailures = [new ActionFailure { Name = "Probe", Reason = new Reason { Message = "infra down" } }],
        };

        var passed = assertion.Assert(Sessions(session), NoDataSources);

        Assert.That(passed, Is.False, "a session failure must not be hidden behind passing flows");
        Assert.That(assertion.AssertionMessage, Does.Contain("session failure"));
    }

    [Test]
    public void Assert_OutcomesRecordedWithoutASessionScope_AreStillReported()
    {
        var (assertion, context) = NewAssertion();
        PlaywrightFlowResults.Record(
            context, PlaywrightFlowResults.UnscopedSessionName, new PlaywrightFlowOutcome("Todo", Passed: false, "boom"));

        var passed = assertion.Assert(Sessions(new SessionData { Name = "Journey" }), NoDataSources);

        Assert.That(passed, Is.False, "a missing session scope must degrade gracefully, not silently pass");
        Assert.That(assertion.AssertionMessage, Does.Contain("Todo"));
    }
}
