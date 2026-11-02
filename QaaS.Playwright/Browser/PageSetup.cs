using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using QaaS.Playwright.Configuration;

namespace QaaS.Playwright.Browser;

/// <summary>
/// Gives each page of a run the run's settings. A context the run owns already gave its pages the viewport and the
/// mouse; a borrowed one did not, so each page the run opens gets them, and no other page of the context.
/// </summary>
internal sealed class PageSetup(PlaywrightFlowConfig config, bool ownsContext, ILogger logger)
{
    private static readonly string[] Assets =
    [
        "*.png", "*.jpg", "*.jpeg", "*.gif", "*.svg", "*.ico", "*.webp", "*.avif",
        "*.woff", "*.woff2", "*.ttf", "*.eot",
    ];

    /// <summary>The run's own page: its settings, and a warning when the browser will not show what is expected.</summary>
    public async Task ApplyToRunPageAsync(IPage page)
    {
        await ApplyAsync(page);
        if (!config.EmulateDesktopPointer) await DesktopPointer.WarnIfMissingAsync(page, logger);
        if (!config.Headless) await WarnIfHeadlessAsync(page);
    }

    /// <summary>Any page of the run, pop-ups included. The timeout is set before the first await, so at once.</summary>
    public async Task ApplyAsync(IPage page)
    {
        page.SetDefaultTimeout(config.DefaultTimeout);
        if (!ownsContext) await SetViewportAsync(page);
        if (config.Headless && config.BlockAssets) await BlockAssetsAsync(page);
        if (!ownsContext && config.EmulateDesktopPointer) await DesktopPointer.EmulateAsync(page);
    }

    public static void WarnNoViewport(PlaywrightFlowConfig config, ILogger logger, PlaywrightException failure) =>
        logger.LogWarning("Could not set the viewport to {Width}x{Height}, so the run goes on at the browser's own " +
                          "size: {Message}", config.ViewportWidth, config.ViewportHeight, failure.Message);

    private async Task SetViewportAsync(IPage page)
    {
        try
        {
            await page.SetViewportSizeAsync(config.ViewportWidth, config.ViewportHeight);
        }
        catch (PlaywrightException failure) when (!page.IsClosed)
        {
            WarnNoViewport(config, logger, failure);
        }
    }

    // Through CDP rather than a Playwright route, which would turn off the HTTP cache. Only a speed-up: a browser that
    // refuses it, as some remote ones do, runs the flows with the assets loading.
    private async Task BlockAssetsAsync(IPage page)
    {
        try
        {
            var cdp = await page.Context.NewCDPSessionAsync(page);
            await cdp.SendAsync("Network.enable");
            await cdp.SendAsync("Network.setBlockedURLs", new Dictionary<string, object> { ["urls"] = Assets });
        }
        catch (PlaywrightException failure) when (!page.IsClosed)
        {
            logger.LogWarning("Could not block images and fonts, so they load: {Message}", failure.Message);
        }
    }

    // Headless: false only slows the run down for a person; whether a window shows depends on how Chrome started.
    private async Task WarnIfHeadlessAsync(IPage page)
    {
        var userAgent = await page.EvaluateAsync<string>("() => navigator.userAgent");
        if (userAgent.Contains("HeadlessChrome", StringComparison.Ordinal))
            logger.LogWarning("Headless: false, but the Chrome at {Url} runs headless, so no window will appear. Start " +
                              "one with a window to watch the run.", BrowserUrl.Redact(BrowserConnector.UrlOf(config)));
    }
}
