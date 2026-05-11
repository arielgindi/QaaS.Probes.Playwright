using QaaS.Probes.Playwright.Recorder;

namespace QaaS.Probes.Playwright.Tests;

[TestFixture]
public class FlowCodeGeneratorTests
{
    [Test]
    public void ExtractActions_FullCodegenOutput_ReturnsOnlyActionLines()
    {
        var code = """
            using Microsoft.Playwright;
            [TestFixture]
            public class Tests : PageTest
            {
                public async Task MyTest()
                {
                    await Page.GotoAsync("https://example.com");
                    await Page.GetByLabel("User").FillAsync("admin");
                    await Page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();
                }
            }
            """;

        var result = FlowCodeGenerator.ExtractActions(code);

        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result[0], Does.StartWith("await page.GotoAsync"));
        Assert.That(result[1], Does.Contain("FillAsync"));
        Assert.That(result[2], Does.Contain("ClickAsync"));
    }

    [Test]
    public void ExtractActions_NormalizesPageToLowercase() =>
        Assert.That(FlowCodeGenerator.ExtractActions("""await Page.GotoAsync("https://x.com");""")[0],
            Does.StartWith("await page."));

    [Test]
    public void ExtractActions_EmptyInput_ReturnsEmpty() =>
        Assert.That(FlowCodeGenerator.ExtractActions(""), Is.Empty);

    [TestCase("login-flow", "LoginFlow")]
    [TestCase("add-to-cart", "AddToCart")]
    [TestCase("LoginFlow",  "LoginFlow")]
    [TestCase("--login",    "Login")]      // empty segments are filtered
    [TestCase("login_",     "Login")]
    [TestCase("a b c",      "ABC")]
    public void ToPascalCase_Converts(string input, string expected) =>
        Assert.That(FlowCodeGenerator.ToPascalCase(input), Is.EqualTo(expected));

    [Test]
    public void ToPascalCase_NoUsableChars_Throws() =>
        Assert.Throws<ArgumentException>(() => FlowCodeGenerator.ToPascalCase("---"));

    [Test]
    public void Render_ProducesCompilableShape()
    {
        var generated = FlowCodeGenerator.Render("LoginFlow",
            ["await page.GotoAsync(\"x\");", "await page.ClickAsync(\"#go\");"]);

        Assert.That(generated, Does.Contain("public class LoginFlow : BasePlaywrightFlow<LoginFlowConfig>"));
        Assert.That(generated, Does.Contain("await page.GotoAsync"));
        Assert.That(generated, Does.Contain("public record LoginFlowConfig"));
    }
}
