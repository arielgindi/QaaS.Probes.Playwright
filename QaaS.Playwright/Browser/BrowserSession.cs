using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using QaaS.Playwright.Configuration;

namespace QaaS.Playwright.Browser;

/// <summary>
/// The browser context and the page one probe run works in, on the process's shared connection to Chrome. Disposing it
/// closes what the run opened, and nothing it did not open.
/// </summary>
internal sealed class BrowserSession(IBrowserContext context, bool ownsContext, IPage page, ILogger logger)
    : IAsyncDisposable
{
    private static readonly DateTime ProcessStartedAt = Process.GetCurrentProcess().StartTime.ToUniversalTime();

    private static readonly string[] AssetPatterns =
        ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.svg", "*.ico", "*.woff", "*.woff2", "*.ttf", "*.eot"];

    public IPage Page => page;

    /// <summary>When true, disposing leaves the page open: a person is looking at it.</summary>
    public bool KeepPageOpen { get; set; }

    public static async Task<BrowserSession> OpenAsync(PlaywrightFlowConfig config, ILogger logger)
    {
        var browser = await SharedBrowser.GetAsync(config, logger);
        var (context, ownsContext) = await OpenContextAsync(browser, config, logger);
        try
        {
            var page = await context.NewPageAsync();
            await SetUpPageAsync(page, config, logger);
            return new BrowserSession(context, ownsContext, page, logger);
        }
        catch
        {
            // The connection outlives this run, so nothing else would close the context.
            if (ownsContext) await context.DisposeAsync();
            throw;
        }
    }

    /// <summary>Writes the context's cookies and localStorage to <paramref name="path"/> for later sessions.</summary>
    public async Task SaveStorageStateAsync(string path)
    {
        var savedPath = await AtomicFileWriter.WriteAsync(path, await context.StorageStateAsync());
        logger.LogInformation("Saved storage state to {Path}", savedPath);
    }

    public async ValueTask DisposeAsync()
    {
        if (KeepPageOpen) return;
        try
        {
            // Disposing an own context closes its page too.
            if (ownsContext) await context.DisposeAsync();
            else await page.CloseAsync();
        }
        catch (Exception failure)
        {
            // A teardown error must not hide the flow's own failure.
            logger.LogWarning("Could not close the page during teardown: {Message}", failure.Message);
        }
    }

    // A run gets a fresh context of its own unless it opts out with IsolateContext: false; then it shares the
    // browser's default context, unless it starts from a saved login, which can only seed a new context.
    private static async Task<(IBrowserContext Context, bool Owned)> OpenContextAsync(
        IBrowser browser, PlaywrightFlowConfig config, ILogger logger)
    {
        var storageState = StorageStateToLoad(config, logger);
        if (!config.IsolateContext && storageState is null && browser.Contexts.Count > 0)
            return (browser.Contexts[0], false);

        return (await browser.NewContextAsync(new BrowserNewContextOptions { StorageStatePath = storageState }), true);
    }

    private static string? StorageStateToLoad(PlaywrightFlowConfig config, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(config.LoadStorageStatePath)) return null;

        var path = Path.GetFullPath(config.LoadStorageStatePath);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"LoadStorageStatePath '{path}' does not exist. The session that saves it (SaveStorageStatePath) " +
                "must finish first, or remove LoadStorageStatePath.", path);

        // A run that skipped the session saving it, e.g. with -a or a category filter, would reuse an old login.
        var savedAt = File.GetLastWriteTimeUtc(path);
        if (savedAt < ProcessStartedAt)
            logger.LogWarning("LoadStorageStatePath '{Path}' was saved {Hours:0.#} h ago, before this run: did the " +
                              "session that saves it run?", path, (DateTime.UtcNow - savedAt).TotalHours);

        logger.LogInformation("Loading storage state from {Path}", path);
        return path;
    }

    private static async Task SetUpPageAsync(IPage page, PlaywrightFlowConfig config, ILogger logger)
    {
        page.SetDefaultTimeout(config.DefaultTimeout);

        // Some remote browsers reject a viewport override; the run can still go on at the browser's own size.
        try
        {
            await page.SetViewportSizeAsync(config.ViewportWidth, config.ViewportHeight);
        }
        catch (PlaywrightException failure)
        {
            logger.LogWarning("Could not set the viewport to {Width}x{Height}: {Message}",
                config.ViewportWidth, config.ViewportHeight, failure.Message);
        }

        if (config.Headless && config.BlockAssets) await BlockAssetsAsync(page);

        if (config.EmulateDesktopPointer) await DesktopPointer.EmulateAsync(page);
        else await DesktopPointer.WarnIfMissingAsync(page, logger);

        if (!config.Headless) await WarnIfHeadlessAsync(page, config, logger);
    }

    // Headless: false only slows the run down for a person to watch; whether a window shows is up to the Chrome.
    private static async Task WarnIfHeadlessAsync(IPage page, PlaywrightFlowConfig config, ILogger logger)
    {
        var userAgent = await page.EvaluateAsync<string>("() => navigator.userAgent");
        if (userAgent.Contains("HeadlessChrome", StringComparison.Ordinal))
            logger.LogWarning("Headless: false, but the Chrome at {Url} runs headless, so no window will appear. " +
                              "Start one with a window to watch the run.", BrowserUrl.Redact(BrowserConnector.UrlOf(config)));
    }

    // Through CDP rather than a Playwright route: a route turns off the HTTP cache and holds every request for the
    // driver, so the app's bundle would be downloaded again on every navigation.
    private static async Task BlockAssetsAsync(IPage page)
    {
        var cdp = await page.Context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Network.enable");
        await cdp.SendAsync("Network.setBlockedURLs", new Dictionary<string, object> { ["urls"] = AssetPatterns });
    }
}
