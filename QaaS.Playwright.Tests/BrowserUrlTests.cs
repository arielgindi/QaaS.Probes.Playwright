using QaaS.Playwright.Engine;

namespace QaaS.Playwright.Tests;

[TestFixture]
public class BrowserUrlTests
{
    [Test]
    public void Redact_UrlWithQuery_HidesTheToken()
    {
        var redacted = BrowserUrl.Redact("ws://chrome.qa.svc:3000?token=super-secret");

        Assert.That(redacted, Does.Not.Contain("super-secret"));
        Assert.That(redacted, Does.Contain("<redacted>"));
        Assert.That(redacted, Does.Contain("chrome.qa.svc:3000"));
    }

    [Test]
    public void Redact_UrlWithoutQuery_IsUnchanged()
    {
        Assert.That(BrowserUrl.Redact("http://localhost:9222"), Is.EqualTo("http://localhost:9222"));
    }

    [Test]
    public void Redact_NonAbsoluteValue_IsReturnedAsIs()
    {
        Assert.That(BrowserUrl.Redact("not-a-url"), Is.EqualTo("not-a-url"));
    }

    [Test]
    public void EnsureNoTemplatePlaceholder_WithUnresolvedPlaceholder_Throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            BrowserUrl.EnsureNoTemplatePlaceholder("ws://chrome.<your-namespace>.svc:3000?token=<token>"));

    [Test]
    public void EnsureNoTemplatePlaceholder_WithResolvedUrl_DoesNotThrow() =>
        Assert.DoesNotThrow(() => BrowserUrl.EnsureNoTemplatePlaceholder("ws://chrome.qa.svc:3000?token=abc"));
}
