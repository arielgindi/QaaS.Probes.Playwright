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
    public void Assert_ProbeWarnings_AreCountedInTheMessageAndListedInTheTrace()
    {
        var (assertion, context) = NewAssertion();
        PlaywrightFlowResults.Record(context, "Journey", new PlaywrightFlowOutcome("SignIn", Passed: true));
        PlaywrightFlowResults.RecordWarning(context, "Journey", "The browser reports no mouse");
        PlaywrightFlowResults.RecordWarning(context, "Journey", "The browser reports no mouse");
        PlaywrightFlowResults.RecordWarning(context, null, "Probe is running outside a session");

        var passed = assertion.Assert(Sessions(new SessionData { Name = "Journey" }), NoDataSources);

        Assert.That(passed, Is.True, "warnings do not fail the assertion");
        Assert.That(assertion.AssertionMessage, Is.EqualTo(
            "All 1 Playwright flow(s) passed: SignIn. 2 warning(s), see the trace."));
        Assert.That(assertion.AssertionTrace, Does.EndWith(
            "Warnings:\n  - Journey: The browser reports no mouse\n  - (unscoped): Probe is running outside a session"));
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
    public void Assert_SameFlowFailedInTwoSessions_NamesEachScreenshotAfterItsSession()
    {
        // Two attachments with one path make the Allure reporter abort the whole run.
        var (assertion, context) = NewAssertion();
        PlaywrightFlowResults.Record(context, "Ui 1", new PlaywrightFlowOutcome("Login", Passed: false, "boom", [1]));
        PlaywrightFlowResults.Record(context, "Ui 2", new PlaywrightFlowOutcome("Login", Passed: false, "boom", [2]));

        assertion.Assert(Sessions(new SessionData { Name = "Ui 1" }, new SessionData { Name = "Ui 2" }), NoDataSources);

        string[] expected = ["Ui_1-Login-failure.png", "Ui_2-Login-failure.png"];
        Assert.That(assertion.AssertionAttachments.Select(attachment => attachment.Path), Is.EqualTo(expected));
    }

    [Test]
    public void Assert_SameFlowFailedInTwoProbesOfOneSession_NamesEachScreenshotAfterItsProbe()
    {
        var (assertion, context) = NewAssertion();
        PlaywrightFlowResults.Record(context, "Leave",
            new PlaywrightFlowOutcome("Login", Passed: false, "boom", [1], ProbeName: "Employee"));
        PlaywrightFlowResults.Record(context, "Leave",
            new PlaywrightFlowOutcome("Login", Passed: false, "boom", [2], ProbeName: "Manager"));

        assertion.Assert(Sessions(new SessionData { Name = "Leave" }), NoDataSources);

        string[] expected = ["Leave-Employee-Login-failure.png", "Leave-Manager-Login-failure.png"];
        Assert.That(assertion.AssertionAttachments.Select(attachment => attachment.Path), Is.EqualTo(expected));
    }

    [Test]
    public void Assert_ScreenshotsThatWouldShareAPath_GetUniquePaths()
    {
        // Probes of one session under a runner that does not name them; the reporter compares paths ignoring case.
        var (assertion, context) = NewAssertion();
        PlaywrightFlowResults.Record(context, "Journey", new PlaywrightFlowOutcome("Login", Passed: false, "a", [1]));
        PlaywrightFlowResults.Record(context, "Journey", new PlaywrightFlowOutcome("LOGIN", Passed: false, "b", [2]));

        assertion.Assert(Sessions(new SessionData { Name = "Journey" }), NoDataSources);

        string[] expected = ["Journey-Login-failure.png", "Journey-LOGIN-failure-2.png"];
        Assert.That(assertion.AssertionAttachments.Select(attachment => attachment.Path), Is.EqualTo(expected));
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
    public void Assert_FlowTimedOut_HeadlineNamesTheElementItWaitedForAndThePage()
    {
        var (assertion, context) = NewAssertion();
        PlaywrightFlowResults.Record(context, "Journey", new PlaywrightFlowOutcome("PlaceOrder", Passed: false,
            "Timeout 3000ms exceeded.\nCall log:\n  - waiting for GetByRole(AriaRole.Button, new() { Name = \"Place order\" })\n",
            FailureUrl: "http://app.test/orders"));

        assertion.Assert(Sessions(new SessionData { Name = "Journey" }), NoDataSources);

        Assert.That(assertion.AssertionMessage, Is.EqualTo(
            "PlaceOrder failed (0/1 flows passed): Timeout 3000ms exceeded " +
            "(waiting for GetByRole(AriaRole.Button, new() { Name = \"Place order\" })) on http://app.test/orders. Passed: none."));
        Assert.That(assertion.AssertionTrace, Does.Contain("Page: http://app.test/orders"));
    }

    [Test]
    public void Assert_ExpectFailed_HeadlineNamesTheElementFromItsCallLog()
    {
        // An Expect() call log opens with the assertion step; the element is on a later line.
        var (assertion, context) = NewAssertion();
        PlaywrightFlowResults.Record(context, "Journey", new PlaywrightFlowOutcome("Todo", Passed: false,
            "Locator expected to be visible\nCall log:\n  - Expect \"ToBeVisibleAsync\" with timeout 5000ms\n" +
            "  - waiting for GetByText(\"Saved\")\n"));

        assertion.Assert(Sessions(new SessionData { Name = "Journey" }), NoDataSources);

        Assert.That(assertion.AssertionMessage, Does.Contain("Locator expected to be visible (waiting for GetByText(\"Saved\"))."));
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
    public void Assert_SessionRanNoFlow_FailsAndNamesTheSession()
    {
        // e.g. an empty Flows list, a misspelled 'Flow:' key, or a session without a probe.
        var (assertion, _) = NewAssertion();

        var passed = assertion.Assert(Sessions(new SessionData { Name = "Journey" }), NoDataSources);

        Assert.That(passed, Is.False, "a session that ran no flow verified nothing");
        Assert.That(assertion.AssertionStatus, Is.EqualTo(AssertionOutcome.Failed));
        Assert.That(assertion.AssertionMessage, Does.Contain("session(s) Journey").And.Contains("nothing was verified"));
        Assert.That(assertion.AssertionTrace, Does.Contain("---- nothing verified ----").And.Contains("Likely causes"));
    }

    [Test]
    public void Assert_OneOfTwoSessionsRanNoFlow_FailsAndNamesOnlyThatSession()
    {
        var (assertion, context) = NewAssertion();
        PlaywrightFlowResults.Record(context, "Ui", new PlaywrightFlowOutcome("SignIn", Passed: true));

        var passed = assertion.Assert(
            Sessions(new SessionData { Name = "Ui" }, new SessionData { Name = "Forgotten" }), NoDataSources);

        Assert.That(passed, Is.False);
        Assert.That(assertion.AssertionMessage, Does.Contain("session(s) Forgotten,").And.Contains("Passed: SignIn"));
        Assert.That(assertion.AssertionMessage, Does.Not.Contain("Ui,"));
    }

    [Test]
    public void Assert_NoSessionAttached_Fails()
    {
        var (assertion, _) = NewAssertion();

        var passed = assertion.Assert(Sessions(), NoDataSources);

        Assert.That(passed, Is.False);
        Assert.That(assertion.AssertionMessage, Does.Contain("No session is attached"));
        Assert.That(assertion.AssertionTrace, Does.Contain("SessionNames"));
    }

    [Test]
    public void Assert_SessionFailedBeforeItsFirstFlow_IsReportedAsASessionFailureOnly()
    {
        var (assertion, _) = NewAssertion();
        var session = new SessionData
        {
            Name = "Journey",
            SessionFailures = [new ActionFailure { Name = "Probe", Reason = new Reason { Message = "connection refused" } }],
        };

        var passed = assertion.Assert(Sessions(session), NoDataSources);

        Assert.That(passed, Is.False);
        Assert.That(assertion.AssertionMessage, Does.Contain("connection refused").And.Not.Contains("nothing was verified"));
    }

    [Test]
    public void Assert_UnscopedOutcomes_CountForEveryAttachedSession()
    {
        // A probe that ran outside a session scope cannot say which session it belongs to, so its passing flows
        // must not make an attached session look as if it ran nothing.
        var (assertion, context) = NewAssertion();
        PlaywrightFlowResults.Record(context, null, new PlaywrightFlowOutcome("Todo", Passed: true));

        var passed = assertion.Assert(Sessions(new SessionData { Name = "Journey" }), NoDataSources);

        Assert.That(passed, Is.True);
    }

    [Test]
    public void Assert_UnscopedOutcomes_DoNotVerifyAnAssertionWithNoSession()
    {
        var (assertion, context) = NewAssertion();
        PlaywrightFlowResults.Record(context, null, new PlaywrightFlowOutcome("Todo", Passed: true));

        Assert.That(assertion.Assert(Sessions(), NoDataSources), Is.False);
    }

    [Test]
    public void Assert_OutcomesRecordedWithoutASessionScope_AreStillReported()
    {
        var (assertion, context) = NewAssertion();
        PlaywrightFlowResults.Record(context, null, new PlaywrightFlowOutcome("Todo", Passed: false, "boom"));

        var passed = assertion.Assert(Sessions(new SessionData { Name = "Journey" }), NoDataSources);

        Assert.That(passed, Is.False, "a missing session scope must degrade gracefully, not silently pass");
        Assert.That(assertion.AssertionMessage, Does.Contain("Todo"));
    }

    [Test]
    public void Assert_ASessionNamedUnscoped_DoesNotFailAnotherSession()
    {
        // "(unscoped)" is a valid session name; its outcomes are that session's alone.
        var (assertion, context) = NewAssertion();
        PlaywrightFlowResults.Record(context, "(unscoped)", new PlaywrightFlowOutcome("Login", Passed: false, "boom"));
        PlaywrightFlowResults.Record(context, "Other", new PlaywrightFlowOutcome("Verify", Passed: true));

        Assert.That(assertion.Assert(Sessions(new SessionData { Name = "Other" }), NoDataSources), Is.True);
    }

    [Test]
    public void Assert_ASessionNamedUnscoped_DoesNotVerifyAnotherSession()
    {
        var (assertion, context) = NewAssertion();
        PlaywrightFlowResults.Record(context, "(unscoped)", new PlaywrightFlowOutcome("Login", Passed: true));

        Assert.That(assertion.Assert(Sessions(new SessionData { Name = "Empty" }), NoDataSources), Is.False);
    }

    [Test]
    public void Assert_ASessionNamedUnscoped_ReportsItsFlowOnce()
    {
        var (assertion, context) = NewAssertion();
        PlaywrightFlowResults.Record(context, "(unscoped)", new PlaywrightFlowOutcome("Login", Passed: false, "boom", [1]));

        assertion.Assert(Sessions(new SessionData { Name = "(unscoped)" }), NoDataSources);

        Assert.That(assertion.AssertionAttachments, Has.Count.EqualTo(1));
    }
}
