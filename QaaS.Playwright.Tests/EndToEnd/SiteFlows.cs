using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace QaaS.Playwright.Tests.EndToEnd;

public sealed record UserConfig
{
    public string User { get; init; } = "";
}

public sealed record NoConfig;

public sealed class LogInFlow : BasePlaywrightFlow<UserConfig>
{
    public override async Task RunAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/login");
        await page.GetByLabel("Username").FillAsync(Configuration.User);
        await page.GetByRole(AriaRole.Button, new() { Name = "Log in" }).ClickAsync();
    }
}

public sealed class CheckUserFlow : BasePlaywrightFlow<UserConfig>
{
    public override async Task RunAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/whoami");
        await Expect(page.Locator("#user")).ToHaveTextAsync(Configuration.User);
    }
}

public sealed class SubmitOrderFlow : BasePlaywrightFlow<NoConfig>
{
    public override async Task RunAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/order");
        await page.GetByTestId("submit-order").ClickAsync();
        await Expect(page.Locator("#result")).ToHaveTextAsync("submitted");
    }
}
