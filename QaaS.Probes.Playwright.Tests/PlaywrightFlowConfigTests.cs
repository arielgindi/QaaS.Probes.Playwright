using Microsoft.Extensions.Configuration;
using QaaS.Probes.Playwright.Configuration;

namespace QaaS.Probes.Playwright.Tests;

[TestFixture]
public class PlaywrightFlowConfigTests
{
    [Test]
    public void Defaults_AllBrowserFields_AreNull()
    {
        var cfg = new PlaywrightFlowConfig();
        Assert.Multiple(() =>
        {
            Assert.That(cfg.RemoteBrowserUrl, Is.Null);
            Assert.That(cfg.LocalBrowserUrl, Is.Null);
            Assert.That(cfg.BrowserExecutablePath, Is.Null);
        });
    }

    [Test]
    public void Binds_AllBrowserFields_FromYamlLikeConfig()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BaseUrl"]               = "https://example.com",
                ["RemoteBrowserUrl"]      = "http://chrome.qaas.internal:9222",
                ["LocalBrowserUrl"]       = "http://localhost:9222",
                ["BrowserExecutablePath"] = "/opt/google/chrome/chrome",
            })
            .Build();

        var bound = new PlaywrightFlowConfig();
        config.Bind(bound);

        Assert.Multiple(() =>
        {
            Assert.That(bound.RemoteBrowserUrl,      Is.EqualTo("http://chrome.qaas.internal:9222"));
            Assert.That(bound.LocalBrowserUrl,       Is.EqualTo("http://localhost:9222"));
            Assert.That(bound.BrowserExecutablePath, Is.EqualTo("/opt/google/chrome/chrome"));
        });
    }
}
