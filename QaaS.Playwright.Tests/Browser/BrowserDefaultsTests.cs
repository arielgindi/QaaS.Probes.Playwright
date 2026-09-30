using QaaS.Playwright.Browser;

namespace QaaS.Playwright.Tests.Browser;

[TestFixture]
public class BrowserDefaultsTests
{
    [Test]
    public void TestIdAttribute_IsPlaywrightsOwnDefault() =>
        Assert.That(BrowserDefaults.TestIdAttribute, Is.EqualTo("data-testid"));

    [Test]
    public void ChromeStartupTimeout_IsReadFromTheEmbeddedFile() =>
        Assert.That(BrowserDefaults.ChromeStartupTimeout, Is.EqualTo(TimeSpan.FromSeconds(60)));
}
