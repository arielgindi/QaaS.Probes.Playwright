using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using QaaS.Playwright.Configuration;

namespace QaaS.Playwright.Browser;

/// <summary>
/// The browser side of one probe run: the Playwright driver, the connection to Chrome, the context the flows share
/// and the page they run on. Disposing it closes everything the run opened, and nothing it did not open.
/// </summary>
internal sealed class BrowserSession : IAsyncDisposable
{
    private const string AssetPattern = "**/*.{png,jpg,jpeg,gif,svg,ico,woff,woff2,ttf,eot}";

    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;
    private readonly IBrowserContext _context;
    private readonly bool _ownsContext;
    private readonly ILogger _logger;

    private BrowserSession(
        IPlaywright playwright, IBrowser browser, IBrowserContext context, bool ownsContext, IPage page, ILogger logger)
    {
        _playwright = playwright;
        _browser = browser;
        _context = context;
        _ownsContext = ownsContext;
        _logger = logger;
        Page = page;
    }

    public IPage Page { get; }

    /// <summary>When true, disposing leaves the page open (a person is looking at it).</summary>
    public bool KeepPageOpen { get; set; }

    public static async Task<BrowserSession> OpenAsync(PlaywrightFlowConfig config, ILogger logger)
    {
        var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        try
        {
            var browser = await new BrowserConnector(logger).ConnectAsync(playwright, config);
            var (context, ownsContext) = await OpenContextAsync(browser, config, logger);
            var page = await context.NewPageAsync();
            await SetUpPageAsync(page, config, logger);
            return new BrowserSession(playwright, browser, context, ownsContext, page, logger);
        }
        catch
        {
            playwright.Dispose();
            throw;
        }
    }

    /// <summary>Writes the context's cookies and localStorage to <paramref name="path"/> for later sessions.</summary>
    public async Task SaveStorageStateAsync(string path)
    {
        var savedPath = await AtomicFileWriter.WriteAsync(path, await _context.StorageStateAsync());
        _logger.LogInformation("Saved storage state to {Path}", savedPath);
    }

    public async ValueTask DisposeAsync()
    {
        // Each step is guarded so a teardown error never hides the flow's own failure or skips the next step.
        if (!KeepPageOpen) await TryAsync("close page", () => Page.CloseAsync());
        if (_ownsContext) await TryAsync("dispose browser context", () => _context.DisposeAsync().AsTask());
        await TryAsync("disconnect from browser", () => _browser.DisposeAsync().AsTask());
        _playwright.Dispose();
    }

    // One rule: a run gets a fresh context of its own when it asks for isolation or starts from a saved login (storage
    // state can only seed a new context). Otherwise it shares the browser's default context, so a local Chrome keeps
    // its logins.
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

        if (config.Headless && config.BlockAssets) await page.RouteAsync(AssetPattern, route => route.AbortAsync());
    }

    private async Task TryAsync(string action, Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (Exception failure)
        {
            _logger.LogWarning("Failed to {Action} during teardown: {Message}", action, failure.Message);
        }
    }
}
