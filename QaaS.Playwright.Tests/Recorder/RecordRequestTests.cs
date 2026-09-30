using QaaS.Playwright.Recorder;

namespace QaaS.Playwright.Tests.Recorder;

[TestFixture]
public class RecordRequestTests
{
    [Test]
    public void Parse_NameAndUrl_UsesTheFlowsFolder() =>
        Assert.That(RecordRequest.Parse(["record", "add-to-cart", "https://shop.test"]),
            Is.EqualTo(new RecordRequest("AddToCart", "https://shop.test", "Flows")));

    [Test]
    public void Parse_OutputDirBeforeThePositionals_IsAccepted() =>
        Assert.That(RecordRequest.Parse(["record", "--output-dir", "UiFlows", "login", "https://shop.test"]).OutputDir,
            Is.EqualTo("UiFlows"));

    [TestCase("record", "login", "https://shop.test", "--output-dir")]
    [TestCase("record", "login", "https://shop.test", "--verbose")]
    [TestCase("record", "login", "shop.test")]
    [TestCase("record", "login")]
    [TestCase("replay", "login", "https://shop.test")]
    public void Parse_InvalidArguments_Throw(params string[] args) =>
        Assert.Throws<ArgumentException>(() => RecordRequest.Parse(args));
}
