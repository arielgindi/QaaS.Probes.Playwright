using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace QaaS.Playwright.Tests.EndToEnd;

public sealed record UserConfig
{
    public string User { get; init; } = "";
}

public sealed record NoConfig;

public sealed record PasswordConfig
{
    public string Password { get; init; } = "";
}

public sealed class LogInFlow : BasePlaywrightFlow<UserConfig>
{
    public override async Task RunAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/login");
        await page.GetByLabel("Username").FillAsync(Configuration.User);
        await page.GetByRole(AriaRole.Button, new() { Name = "Log in" }).ClickAsync();
        await page.WaitForURLAsync("**/whoami");
    }
}

/// <summary>
/// Plants the token the gated app needs before any page loads, as a login behind a proxy does, then logs in on
/// <c>{BaseUrl}/login</c>.
/// </summary>
public sealed class GatedLogInFlow : BasePlaywrightFlow<UserConfig>
{
    public override async Task RunAsync(IPage page)
    {
        await page.AddInitScriptAsync("try { localStorage.setItem('gate', 'open'); } catch { }");
        await page.Context.ClearCookiesAsync();
        await page.GotoAsync($"{BaseUrl}/login");
        await page.GetByLabel("Username").FillAsync(Configuration.User);
        await page.GetByRole(AriaRole.Button, new() { Name = "Log in" }).ClickAsync();
        await page.WaitForURLAsync("**/gate/home");
    }
}

/// <summary>Types the user name, then waits for ever for a button the page never shows, as a login that hangs does.</summary>
public sealed class HangingLogInFlow : BasePlaywrightFlow<NoConfig>
{
    public override async Task RunAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/login");
        await page.GetByLabel("Username").FillAsync("alice");
        await page.GetByRole(AriaRole.Button, new() { Name = "Continue with single sign-on" }).ClickAsync(new() { Timeout = 0 });
    }
}

/// <summary>Fills a password into a read-only field, so the timeout's call log quotes it.</summary>
public sealed class FillReadOnlyPasswordFlow : BasePlaywrightFlow<PasswordConfig>
{
    public override async Task RunAsync(IPage page)
    {
        await page.SetContentAsync("<input type='password' readonly>");
        await page.Locator("input").FillAsync(Configuration.Password);
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

public sealed class PlaceMissingOrderFlow : BasePlaywrightFlow<NoConfig>
{
    public override async Task RunAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/order");
        await page.GetByRole(AriaRole.Button, new() { Name = "Place order" }).ClickAsync();
    }
}

/// <summary>Creates the ticket its ForEach item names, e.g. <c>{ "name": "Apollo" }</c>.</summary>
public sealed class CreateTicketFlow : BasePlaywrightFlow<NoConfig>
{
    public override async Task RunAsync(IPage page)
    {
        var name = Item!["name"]!.GetValue<string>();
        await page.GotoAsync($"{BaseUrl}/tickets/new");
        await page.GetByLabel("Name").FillAsync(name);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();

        var created = await page.Locator("#created").TextContentAsync();
        if (created != name)
            throw new InvalidOperationException($"Ticket {ItemIndex} '{name}' was not created: the app says '{created}'.");
    }
}
