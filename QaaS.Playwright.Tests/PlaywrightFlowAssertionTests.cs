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
        // The screenshot must be stored verbatim — a SerializationType would BinaryFormatter-frame the PNG and
        // corrupt it so no image viewer could open it.
        Assert.That(assertion.AssertionAttachments[0].SerializationType, Is.Null);
        Assert.That(assertion.AssertionAttachments[0].Data, Is.EqualTo(new byte[] { 1, 2, 3 }));
    }

    [Test]
    public void Assert_FlowFailed_MessageIsAOneLinerWithoutTheCallLog()
    {
        var (assertion, context) = NewAssertion();
        PlaywrightFlowResults.Record(context, "Journey", new PlaywrightFlowOutcome("SignIn", Passed: true));
        PlaywrightFlowResults.Record(context, "Journey", new PlaywrightFlowOutcome(
            "Todo", Passed: false, "Locator expected to have count '3'\nBut was: '4'\nCall log:\n  - waiting for X"));

        assertion.Assert(Sessions(new SessionData { Name = "Journey" }), NoDataSources);

        Assert.That(assertion.AssertionMessage, Does.Contain("Todo failed"));
        Assert.That(assertion.AssertionMessage, Does.Contain("1/2 flows passed"), "the headline reports the count");
        Assert.That(assertion.AssertionMessage, Does.Contain("Locator expected to have count '3' But was: '4'"));
        Assert.That(assertion.AssertionMessage, Does.Not.Contain("Call log"), "the call log belongs in the trace");
        Assert.That(assertion.AssertionMessage, Does.Not.Contain("\n"), "the headline must stay on one line");
        Assert.That(assertion.AssertionMessage, Does.Contain("Passed: SignIn"));
    }

    [Test]
    public void Assert_FlowFailed_TraceIsAChecklistWithDelimitedFailureDetail()
    {
        var (assertion, context) = NewAssertion();
        PlaywrightFlowResults.Record(context, "Journey", new PlaywrightFlowOutcome("SignIn", Passed: true));
        PlaywrightFlowResults.Record(context, "Journey", new PlaywrightFlowOutcome(
            "Todo", Passed: false, "expected 3\nCall log:\n  - waiting"));

        assertion.Assert(Sessions(new SessionData { Name = "Journey" }), NoDataSources);

        var trace = assertion.AssertionTrace!;
        Assert.That(trace, Does.Contain("1 of 2 flow(s) passed, 1 failed"), "summary line");
        Assert.That(trace, Does.Contain("[PASS]  SignIn"), "passed flow is marked");
        Assert.That(trace, Does.Contain("[FAIL]  Todo"), "failed flow is marked");
        Assert.That(trace, Does.Contain("---- Todo failed ----"), "delimited failure section");
        Assert.That(trace, Does.Contain("Call log"), "the full detail (incl. call log) lives in the trace");
        Assert.That(trace, Does.Not.Contain("✓").And.Not.Contain("✗").And.Not.Contain("──"),
            "no decorative glyphs that mojibake in logs/CI");
    }

    [Test]
    public void Assert_FlowFailureAlsoSurfacedAsSessionFailure_NotReportedTwiceInTrace()
    {
        var (assertion, context) = NewAssertion();
        const string failureMessage = "count mismatch";
        PlaywrightFlowResults.Record(context, "Journey", new PlaywrightFlowOutcome("Todo", Passed: false, failureMessage));
        var session = new SessionData
        {
            Name = "Journey",
            // The probe re-throws the flow exception, so the runner records the same message as a session failure.
            SessionFailures = [new ActionFailure { Name = "Probe", Reason = new Reason { Message = failureMessage } }],
        };

        assertion.Assert(Sessions(session), NoDataSources);

        var occurrences = assertion.AssertionTrace!.Split(failureMessage).Length - 1;
        Assert.That(occurrences, Is.EqualTo(1), "the re-thrown flow failure must not be listed twice");
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
