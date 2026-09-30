using Microsoft.Extensions.Logging;
using QaaS.Playwright.Browser;
using QaaS.Playwright.Configuration;
using QaaS.Playwright.Tests.EndToEnd;

namespace QaaS.Playwright.Tests.Browser;

[TestFixture]
[Category("EndToEnd")]
public class BrowserSessionTests
{
    // Reports no mouse, so setting up a page on it warns.
    private HeadlessChrome _chrome = null!;

    [OneTimeSetUp]
    public async Task StartChrome() => _chrome = await HeadlessChrome.StartAsync();

    [OneTimeTearDown]
    public void StopChrome() => _chrome?.Dispose();

    [Test]
    public async Task OpenAsync_SetupFailingInABorrowedContext_ClosesThePageItOpened()
    {
        var config = new PlaywrightFlowConfig { BaseUrl = "http://app.test", BrowserUrl = _chrome.Url, IsolateContext = false };
        var context = (await SharedBrowser.GetAsync(config, new ListLogger())).Contexts[0];
        var pagesBefore = context.Pages.Count;

        var failure = Assert.ThrowsAsync<InvalidOperationException>(
            () => BrowserSession.OpenAsync(config, new FailingOnWarning()));

        Assert.That(failure!.Message, Does.StartWith("Setup failed on: The browser reports no mouse"));
        Assert.That(context.Pages, Has.Count.EqualTo(pagesBefore));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task OpenAsync_OwnOrBorrowedContext_HasTheConfiguredViewport(bool isolateContext)
    {
        var config = new PlaywrightFlowConfig
        {
            BaseUrl = "http://app.test", BrowserUrl = _chrome.Url, IsolateContext = isolateContext,
            ViewportWidth = 1234, ViewportHeight = 567,
        };

        await using var session = await BrowserSession.OpenAsync(config, new ListLogger());

        Assert.That(await session.Page.EvaluateAsync<int[]>("() => [innerWidth, innerHeight]"), Is.EqualTo(new[] { 1234, 567 }));
    }

    // Fails the page's setup after the page was opened.
    private sealed class FailingOnWarning : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                throw new InvalidOperationException($"Setup failed on: {formatter(state, exception)}");
        }
    }
}
