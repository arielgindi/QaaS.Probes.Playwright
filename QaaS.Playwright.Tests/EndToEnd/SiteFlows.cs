using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace QaaS.Playwright.Tests.EndToEnd;

public sealed record UserConfig
{
    public string User { get; init; } = "";
}

public sealed record NoConfig;

public sealed record PointerConfig
{
    public string Expected { get; init; } = "";
}

public sealed class LogInFlow : BasePlaywrightFlow<UserConfig>
{
    /// <summary>
    /// When set, every login waits here for the others, so if parallel sessions shared cookies they would all see the
    /// last user to log in.
    /// </summary>
    public static Barrier? AllLoggedIn { get; set; }

    public override async Task RunAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/login");
        await page.GetByLabel("Username").FillAsync(Configuration.User);
        await page.GetByRole(AriaRole.Button, new() { Name = "Log in" }).ClickAsync();
        await page.WaitForURLAsync("**/whoami");
        if (AllLoggedIn is { } barrier) await Task.Run(() => barrier.SignalAndWait(TimeSpan.FromSeconds(10)));
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

public sealed class CheckPointerFlow : BasePlaywrightFlow<PointerConfig>
{
    public override async Task RunAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/pointer");
        await Expect(page.Locator("#pointer")).ToHaveTextAsync(Configuration.Expected);
    }
}
