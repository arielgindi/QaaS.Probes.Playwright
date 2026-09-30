using Microsoft.Extensions.Logging.Abstractions;
using QaaS.Playwright.Browser;
using QaaS.Playwright.Tests.EndToEnd;

namespace QaaS.Playwright.Tests.Browser;

[TestFixture]
public class LocalChromeLauncherTests
{
    [Test]
    public async Task IsReachable_NothingListening_ReturnsFalse() =>
        Assert.That(await LocalChromeLauncher.IsReachableAsync("http://localhost:1"), Is.False);

    [Test]
    public async Task IsReachable_ServerAnswers_ReturnsTrue()
    {
        using var site = TestSite.Start();

        Assert.That(await LocalChromeLauncher.IsReachableAsync(site.Url), Is.True);
    }

    [TestCase("not-a-url")]
    [TestCase("ws://localhost:1")]
    [TestCase("http://localhost")]
    public void EnsureRunning_UrlChromeCannotBeStartedFor_Throws(string url)
    {
        var failure = Assert.ThrowsAsync<ArgumentException>(() =>
            LocalChromeLauncher.EnsureRunningAsync(url, executablePath: null, NullLogger.Instance));

        Assert.That(failure!.Message, Does.Contain("http://localhost:9222"), "shows a URL that works");
    }
}
