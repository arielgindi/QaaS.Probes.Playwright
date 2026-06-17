using Microsoft.Extensions.Logging.Abstractions;
using QaaS.Framework.SDK.ContextObjects;

namespace QaaS.Playwright.Tests;

[TestFixture]
public class PlaywrightFlowResultsTests
{
    private static Context NewContext() => new() { Logger = NullLogger.Instance };

    [Test]
    public void Read_WithNoRecordedOutcomes_ReturnsEmpty()
    {
        Assert.That(PlaywrightFlowResults.Read(NewContext(), "session"), Is.Empty);
    }

    [Test]
    public void Record_ThenRead_ReturnsOutcomesInExecutionOrder()
    {
        var context = NewContext();
        PlaywrightFlowResults.Record(context, "session", new PlaywrightFlowOutcome("First", Passed: true));
        PlaywrightFlowResults.Record(context, "session", new PlaywrightFlowOutcome("Second", Passed: false, "boom"));

        var outcomes = PlaywrightFlowResults.Read(context, "session");

        Assert.That(outcomes.Select(outcome => outcome.FlowName), Is.EqualTo(new[] { "First", "Second" }));
        Assert.That(outcomes[1].Passed, Is.False);
        Assert.That(outcomes[1].FailureMessage, Is.EqualTo("boom"));
    }

    [Test]
    public void Record_IsScopedPerSession()
    {
        var context = NewContext();
        PlaywrightFlowResults.Record(context, "sessionA", new PlaywrightFlowOutcome("Flow", Passed: true));

        Assert.That(PlaywrightFlowResults.Read(context, "sessionA"), Has.Count.EqualTo(1));
        Assert.That(PlaywrightFlowResults.Read(context, "sessionB"), Is.Empty);
    }

    [Test]
    public void Read_ReturnsASnapshot_UnaffectedByLaterRecords()
    {
        var context = NewContext();
        PlaywrightFlowResults.Record(context, "session", new PlaywrightFlowOutcome("First", Passed: true));

        var snapshot = PlaywrightFlowResults.Read(context, "session");
        PlaywrightFlowResults.Record(context, "session", new PlaywrightFlowOutcome("Second", Passed: true));

        Assert.That(snapshot, Has.Count.EqualTo(1));
    }

    [Test]
    public void Read_WhenAnotherComponentSquatsTheKey_ThrowsAClearError()
    {
        var context = NewContext();
        context.InsertValueIntoGlobalDictionary(["PlaywrightFlowResults", "session"], "not a list of outcomes");

        Assert.Throws<InvalidOperationException>(() => PlaywrightFlowResults.Read(context, "session"));
    }
}
