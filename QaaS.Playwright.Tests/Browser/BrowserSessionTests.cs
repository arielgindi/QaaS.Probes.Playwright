using Microsoft.Extensions.Logging;
using QaaS.Playwright.Browser;
using QaaS.Playwright.Configuration;
using QaaS.Playwright.Tests.EndToEnd;

namespace QaaS.Playwright.Tests.Browser;

[TestFixture]
[Category("EndToEnd")]
public class BrowserSessionTests
{
    [Test]
    public async Task OpenAsync_SetupFailingInABorrowedContext_ClosesThePageItOpened()
    {
        using var chrome = await HeadlessChrome.StartAsync(); // Reports no mouse, so setting up the page warns.
        var config = new PlaywrightFlowConfig { BaseUrl = "http://app.test", BrowserUrl = chrome.Url, IsolateContext = false };
        var context = (await SharedBrowser.GetAsync(config, new ListLogger())).Contexts[0];
        var pagesBefore = context.Pages.Count;

        var failure = Assert.ThrowsAsync<InvalidOperationException>(() => BrowserSession.OpenAsync(config, new FailingOnWarning()));

        Assert.That(failure!.Message, Does.StartWith("Setup failed on: The browser reports no mouse"));
        Assert.That(context.Pages, Has.Count.EqualTo(pagesBefore));
    }

    // Fails the page's setup after the page was opened.
    private sealed class FailingOnWarning : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) throw new InvalidOperationException($"Setup failed on: {formatter(state, exception)}");
        }
    }
}
