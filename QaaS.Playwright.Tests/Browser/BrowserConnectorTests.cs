using QaaS.Playwright.Browser;
using QaaS.Playwright.Configuration;

namespace QaaS.Playwright.Tests.Browser;

[TestFixture]
public class BrowserConnectorTests
{
    private const string Remote = "ws://browserless.example.com:3000?token=abc";

    [TestCase(Remote, true, null, Remote)]
    [TestCase(Remote, true, "cluster", Remote)]
    [TestCase(Remote, true, "local", BrowserConnector.LocalUrl)]
    [TestCase(Remote, true, " LOCAL ", BrowserConnector.LocalUrl)]
    [TestCase(Remote, false, null, BrowserConnector.LocalUrl)]
    [TestCase("http://localhost:9333", false, "local", "http://localhost:9333")]
    [TestCase(null, true, null, "http://localhost:9222")]
    public void Browser_IsTheLocalChrome_WithEnvLocalOrHeadlessFalse(string? browserUrl, bool headless, string? env, string expected) =>
        Assert.That(BrowserConnector.UrlOf(new PlaywrightFlowConfig { BrowserUrl = browserUrl, Headless = headless }, env),
            Is.EqualTo(expected));
}
