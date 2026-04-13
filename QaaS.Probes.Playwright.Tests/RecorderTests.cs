using QaaS.Probes.Playwright.Recorder;

namespace QaaS.Probes.Playwright.Tests;

[TestFixture]
public class RecorderTests
{
    [Test]
    public void ExtractActions_FullCodegenOutput_ReturnsOnlyActionLines()
    {
        var code = """
            using Microsoft.Playwright;
            [TestFixture]
            public class Tests : PageTest
            {
                [Test]
                public async Task MyTest()
                {
                    await Page.GotoAsync("https://example.com");
                    await Page.GetByLabel("User").FillAsync("admin");
                    await Page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();
                }
            }
            """;

        var result = Program.ExtractActions(code);

        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result[0], Does.StartWith("await page.GotoAsync"));
        Assert.That(result[1], Does.Contain("FillAsync"));
        Assert.That(result[2], Does.Contain("ClickAsync"));
    }

    [Test]
    public void ExtractActions_NormalizesPageToLowercase()
    {
        var code = """await Page.GotoAsync("https://x.com");""";
        var result = Program.ExtractActions(code);
        Assert.That(result[0], Does.StartWith("await page."));
    }

    [Test]
    public void ExtractActions_EmptyInput_ReturnsEmpty()
    {
        Assert.That(Program.ExtractActions(""), Is.Empty);
    }

    [Test]
    public void ToPascalCase_Converts()
    {
        Assert.That(Program.ToPascalCase("login-flow"), Is.EqualTo("LoginFlow"));
        Assert.That(Program.ToPascalCase("add-to-cart"), Is.EqualTo("AddToCart"));
        Assert.That(Program.ToPascalCase("LoginFlow"), Is.EqualTo("LoginFlow"));
    }
}
