using QaaS.Playwright.Engine;

namespace QaaS.Playwright.Tests;

[TestFixture]
public class BrowserModeResolverTests
{
    [TestCase(null, BrowserMode.Cluster)]
    [TestCase("", BrowserMode.Cluster)]
    [TestCase("   ", BrowserMode.Cluster)]
    [TestCase("local", BrowserMode.Local)]
    [TestCase("LOCAL", BrowserMode.Local)]
    [TestCase(" Local ", BrowserMode.Local)]
    [TestCase("cluster", BrowserMode.Cluster)]
    [TestCase("remote", BrowserMode.Cluster)]
    public void Parse_KnownValues(string? input, BrowserMode expected) =>
        Assert.That(BrowserModeResolver.Parse(input), Is.EqualTo(expected));

    [TestCase("true")]
    [TestCase("1")]
    [TestCase("on")]
    [TestCase("localhost")]
    [TestCase("yes")]
    public void Parse_UnknownValue_ThrowsWithHelpfulMessage(string input)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BrowserModeResolver.Parse(input));
        Assert.That(ex!.Message, Does.Contain(BrowserModeResolver.EnvVar));
        Assert.That(ex.Message, Does.Contain("local"));
    }
}
