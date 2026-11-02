using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Playwright.Browser;
using AssertionOutcome = QaaS.Framework.SDK.Hooks.Assertion.AssertionStatus;

namespace QaaS.Playwright.Tests.EndToEnd;

/// <summary>ForEach: one flow run per DataSource item, spread over parallel workers.</summary>
[TestFixture]
[Category("EndToEnd")]
public class ForEachEndToEndTests
{
    private TestSite _site = null!;
    private HeadlessChrome _chrome = null!;

    [OneTimeSetUp]
    public async Task StartChromeAndSite()
    {
        _chrome = await HeadlessChrome.StartAsync(DesktopPointer.LaunchFlags);
        _site = TestSite.Start();
    }

    [OneTimeTearDown]
    public void StopChromeAndSite()
    {
        _chrome?.Dispose();
        _site?.Dispose();
    }

    [Test]
    public void TwelveItemsOnFourWorkers_AllPass_EachReported_OneLoginPerWorker()
    {
        var run = new QaasRun();
        string[] names = [.. Enumerable.Range(0, 12).Select(index => $"twelve-{index}")];
        var loginsBefore = _site.Logins;

        var assertion = run.RunAssertion(run.RunSession("Tickets", Settings(parallelism: 4), Tickets(names)));

        Assert.That(assertion.AssertionStatus, Is.EqualTo(AssertionOutcome.Passed), assertion.AssertionTrace);
        Assert.That(assertion.AssertionMessage, Does.StartWith("All 16 Playwright flow(s) passed"));
        Assert.That(assertion.AssertionMessage, Does.Contain("CreateTicketFlow[0]").And.Contains("CreateTicketFlow[11]"));
        Assert.That(_site.Tickets.Where(names.Contains), Is.EquivalentTo(names), "each item created once");
        Assert.That(_site.Logins - loginsBefore, Is.EqualTo(4), "SetupFlows run once per worker");
    }

    private static DataSource Tickets(IEnumerable<string> names) =>
        ListGenerator.DataSource("Tickets", [.. names.Select(name => $$"""{"name":"{{name}}"}""")]);

    private Dictionary<string, string?> Settings(int parallelism) => new()
    {
        ["BaseUrl"] = _site.Url,
        ["BrowserUrl"] = _chrome.Url,
        ["DefaultTimeout"] = "5000",
        ["SetupFlows:0"] = "LogInFlow",
        ["Flows:0"] = "CreateTicketFlow",
        ["FlowConfiguration:LogInFlow:User"] = "support",
        ["ForEach"] = "Tickets",
        ["Parallelism"] = parallelism.ToString(),
    };
}
