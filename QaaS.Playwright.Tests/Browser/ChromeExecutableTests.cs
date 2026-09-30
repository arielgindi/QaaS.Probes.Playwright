using QaaS.Playwright.Browser;

namespace QaaS.Playwright.Tests.Browser;

[TestFixture]
public class ChromeExecutableTests
{
    [Test]
    public void Resolve_ConfiguredPathThatExists_ReturnsIt()
    {
        var path = Path.GetTempFileName();
        try
        {
            Assert.That(ChromeExecutable.Resolve(path), Is.EqualTo(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Resolve_ConfiguredPathThatDoesNotExist_NamesIt()
    {
        var failure = Assert.Throws<FileNotFoundException>(() => ChromeExecutable.Resolve("/no/such/chrome"));

        Assert.That(failure!.Message, Does.Contain("BrowserExecutablePath '/no/such/chrome'"));
    }
}
