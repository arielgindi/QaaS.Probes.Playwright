using QaaS.Playwright.Recorder;

namespace QaaS.Playwright.Tests.Recorder;

[TestFixture]
public class FlowCodeGeneratorTests
{
    [Test]
    public void ExtractActions_DropsInitialGotoAsync_KeepsRest()
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

        Assert.That(result, Has.Count.EqualTo(2));     // initial GotoAsync stripped
        Assert.That(result[0], Does.Contain("FillAsync"));
        Assert.That(result[1], Does.Contain("ClickAsync"));
    }

    [Test]
    public void ExtractActions_NormalizesPageToLowercase() =>
        Assert.That(
            FlowCodeGenerator.ExtractActions("""await Page.ClickAsync("#go");""")[0],
            Does.StartWith("await page."));

    [Test]
    public void ExtractActions_RejoinsStatementWrappedAcrossLines()
    {
        // Codegen can wrap a long locator chain over several lines; it must come back as one compilable statement.
        var code = """
            await Page.GetByRole(AriaRole.Button, new() { Name = "Save" })
                .GetByText("Confirm")
                .ClickAsync();
            await Page.GetByLabel("Email").FillAsync("a@b.com");
            """;

        var result = FlowCodeGenerator.ExtractActions(code);

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0], Is.EqualTo(
            """await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).GetByText("Confirm").ClickAsync();"""));
        Assert.That(result[1], Does.Contain("FillAsync"));
    }

    [Test]
    public void ExtractActions_OnlyInitialGoto_ReturnsEmpty() =>
        Assert.That(FlowCodeGenerator.ExtractActions("""await Page.GotoAsync("https://x.com");"""), Is.Empty);

    [Test]
    public void ExtractActions_EmptyInput_ReturnsEmpty() =>
        Assert.That(FlowCodeGenerator.ExtractActions(""), Is.Empty);

    [TestCase("login-flow", "LoginFlow")]
    [TestCase("add-to-cart", "AddToCart")]
    [TestCase("LoginFlow", "LoginFlow")]
    [TestCase("--login", "Login")]      // empty segments are filtered
    [TestCase("login_", "Login")]
    [TestCase("a b c", "ABC")]
    public void ToPascalCase_Converts(string input, string expected) =>
        Assert.That(FlowCodeGenerator.ToPascalCase(input), Is.EqualTo(expected));

    [Test]
    public void ToPascalCase_NoUsableChars_Throws() =>
        Assert.Throws<ArgumentException>(() => FlowCodeGenerator.ToPascalCase("---"));

    [Test]
    public void Render_ProducesCompilableShape()
    {
        var generated = FlowCodeGenerator.Render("LoginFlow",
            ["await page.GotoAsync(\"x\");", "await page.ClickAsync(\"#go\");"],
            "MyApp.Flows");

        Assert.That(generated, Does.Contain("namespace MyApp.Flows"));
        Assert.That(generated, Does.Contain("public sealed class LoginFlow : BasePlaywrightFlow<LoginFlowConfig>"));
        Assert.That(generated, Does.Contain("await page.GotoAsync"));
        Assert.That(generated, Does.Contain("public sealed record LoginFlowConfig"));
    }
}
