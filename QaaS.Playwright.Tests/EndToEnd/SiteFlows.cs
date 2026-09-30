using System.Diagnostics;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace QaaS.Playwright.Tests.EndToEnd;

public sealed record UserConfig
{
    public string User { get; init; } = "";
}

public sealed record NoConfig;

public sealed record ExpectedConfig
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

public sealed class PlaceMissingOrderFlow : BasePlaywrightFlow<NoConfig>
{
    public override async Task RunAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/order");
        await page.GetByRole(AriaRole.Button, new() { Name = "Place order" }).ClickAsync();
    }
}

/// <summary>Waits for 30 animation frames, about half a second: a flow whose time depends on the page rendering.</summary>
public sealed class AnimationFlow : BasePlaywrightFlow<NoConfig>
{
    public override Task RunAsync(IPage page) => page.EvaluateAsync("""
        () => new Promise(resolve => {
          let frames = 0;
          const tick = () => ++frames === 30 ? resolve() : requestAnimationFrame(tick);
          requestAnimationFrame(tick);
        })
        """);
}

/// <summary>Opens the image page twice and checks whether its image loaded.</summary>
public sealed class CheckImageFlow : BasePlaywrightFlow<ExpectedConfig>
{
    public override async Task RunAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/image");
        await page.GotoAsync($"{BaseUrl}/image");
        await Expect(page.Locator("#image")).ToHaveTextAsync(Configuration.Expected);
    }
}

/// <summary>Creates the mission its ForEach item names, e.g. <c>{ "name": "Apollo" }</c>.</summary>
public sealed class CreateMissionFlow : BasePlaywrightFlow<NoConfig>
{
    public override async Task RunAsync(IPage page)
    {
        var name = Item!["name"]!.GetValue<string>();
        await page.GotoAsync($"{BaseUrl}/missions/new");
        await page.GetByLabel("Name").FillAsync(name);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();

        var created = await page.Locator("#created").TextContentAsync();
        if (created != name)
            throw new InvalidOperationException($"Mission {ItemIndex} '{name}' was not created: the app says '{created}'.");
    }
}

public sealed class CheckPointerFlow : BasePlaywrightFlow<ExpectedConfig>
{
    public override async Task RunAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/pointer");
        await Expect(page.Locator("#pointer")).ToHaveTextAsync(Configuration.Expected);
    }
}

/// <summary>Opens a pop-up, which opens another, as links that open in a new window do.</summary>
public sealed class OpenPopupsFlow : BasePlaywrightFlow<NoConfig>
{
    public override async Task RunAsync(IPage page)
    {
        var popup = await page.RunAndWaitForPopupAsync(() => page.EvaluateAsync("url => window.open(url)", $"{BaseUrl}/order"));
        await popup.RunAndWaitForPopupAsync(() => popup.EvaluateAsync("url => window.open(url)", $"{BaseUrl}/whoami"));
    }
}

/// <summary>Fails with a wrapped cause, as a flow that catches and rethrows does.</summary>
public sealed class DeclinedCheckoutFlow : BasePlaywrightFlow<NoConfig>
{
    public override Task RunAsync(IPage page) => throw new InvalidOperationException(
        "Checkout failed", new InvalidOperationException("Payment provider declined card: ACCOUNT_DISABLED"));
}

public sealed class EmptyMessageFlow : BasePlaywrightFlow<NoConfig>
{
    public override Task RunAsync(IPage page) => throw new InvalidOperationException("");
}

/// <summary>
/// Kills its Chrome's renderers, as the system does to a page that runs out of memory, then clicks. Run it only on a
/// Chrome of its own.
/// </summary>
public sealed class CrashingFlow : BasePlaywrightFlow<NoConfig>
{
    public override async Task RunAsync(IPage page)
    {
        var chrome = await page.Context.Browser!.NewBrowserCDPSessionAsync();
        var processes = (await chrome.SendAsync("SystemInfo.getProcessInfo"))!.Value.GetProperty("processInfo");
        var renderers = processes.EnumerateArray().Where(process => process.GetProperty("type").GetString() == "renderer");
        foreach (var renderer in renderers) Process.GetProcessById(renderer.GetProperty("id").GetInt32()).Kill();

        await page.Locator("h1").ClickAsync(new() { Timeout = 1000 });
    }
}
