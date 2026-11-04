using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using QaaS.Playwright.Configuration;

namespace QaaS.Playwright.Browser;

/// <summary>
/// The browser context and pages of one probe run, on the process's shared connection to Chrome. Every page the run
/// opens, pop-ups included, gets the run's settings. Disposing it closes what the run opened, and nothing else.
/// </summary>
internal sealed class BrowserSession : IAsyncDisposable
{
    // The command that emulates a viewport, which some remote browsers refuse.
    private const string ViewportCommand = "Emulation.setDeviceMetricsOverride";

    private readonly IBrowserContext _context;
    private readonly bool _ownsContext;
    private readonly PageSetup _setup;
    private readonly ILogger _logger;
    private readonly ConcurrentQueue<IPage> _pages = new();
    private readonly ConcurrentDictionary<IPage, bool> _crashed = new();

    private BrowserSession(IBrowserContext context, bool ownsContext, PlaywrightFlowConfig config, ILogger logger)
    {
        _context = context;
        _ownsContext = ownsContext;
        _setup = new PageSetup(config, ownsContext, logger);
        _logger = logger;
    }

    /// <summary>The page the run starts on.</summary>
    public IPage Page { get; private set; } = null!;

    /// <summary>Every page the run opened, in order: <see cref="Page"/> first, then its pop-ups.</summary>
    public IReadOnlyCollection<IPage> Pages => _pages;

    /// <summary>When true, disposing leaves the pages open: a person is looking at them.</summary>
    public bool KeepPageOpen { get; set; }

    /// <summary>Whether the page's renderer crashed, after which every action on it fails.</summary>
    public bool HasCrashed(IPage page) => _crashed.ContainsKey(page);

    public static async Task<BrowserSession> OpenAsync(PlaywrightFlowConfig config, ILogger logger)
    {
        var browser = await SharedBrowser.GetAsync(config, logger);
        try
        {
            return await OpenInAsync(browser, config, logger);
        }
        catch (PlaywrightException) when (!browser.IsConnected)
        {
            // It dropped after this run got it, e.g. because Chrome restarted just before: SharedBrowser connects again.
            return await OpenInAsync(await SharedBrowser.GetAsync(config, logger), config, logger);
        }
    }

    /// <summary>
    /// Opens the run in <paramref name="browser"/>, a connection its caller made, e.g. the recorder's to its own Chrome,
    /// rather than the process's shared one.
    /// </summary>
    public static Task<BrowserSession> OpenAsync(IBrowser browser, PlaywrightFlowConfig config, ILogger logger) =>
        OpenInAsync(browser, config, logger);

    /// <summary>
    /// The run on <paramref name="page"/>, which its caller opened and keeps, e.g. the recorder's recorded page, to go on
    /// from where the run leaves it. The page and its pop-ups get the run's settings; disposing it closes nothing.
    /// </summary>
    public static async Task<BrowserSession> OnPageAsync(IPage page, PlaywrightFlowConfig config, ILogger logger)
    {
        var session = new BrowserSession(page.Context, ownsContext: false, config, logger) { KeepPageOpen = true };
        session.Page = session.Track(page);
        await session._setup.ApplyAsync(page);
        return session;
    }

    /// <summary>
    /// Opens BaseUrl. An error page there fails the run, as flows that start on one verify nothing; a login page's 401
    /// or 403 only warns.
    /// </summary>
    public async Task OpenBaseUrlAsync(string baseUrl)
    {
        var status = (await Page.GotoAsync(baseUrl))?.Status;
        if (status is >= 400 and not (401 or 403))
            throw new InvalidOperationException($"BaseUrl {baseUrl} answered HTTP {status}.");
        if (status >= 400) _logger.LogWarning("BaseUrl {BaseUrl} answered HTTP {Status}; going on.", baseUrl, status);
    }

    /// <summary>
    /// Opens BaseUrl and runs <paramref name="setupFlows"/> on it; or, with SetupFlowsBeforeBaseUrl, runs them first on
    /// the blank page and opens BaseUrl after, unless they left the page on it or under it.
    /// </summary>
    public async Task SetUpAsync(PlaywrightFlowConfig config, Func<Task> setupFlows)
    {
        if (config.SetupFlowsBeforeBaseUrl) await setupFlows();
        if (!config.SetupFlowsBeforeBaseUrl || !IsUnder(Page.Url, config.BaseUrl))
        {
            _logger.LogInformation("Navigating to {BaseUrl}", config.BaseUrl);
            await OpenBaseUrlAsync(config.BaseUrl);
        }

        if (!config.SetupFlowsBeforeBaseUrl) await setupFlows();
    }

    /// <summary>
    /// Whether <paramref name="url"/> is <paramref name="baseUrl"/> or a page under it: the same origin, and its path the
    /// base's or below it.
    /// </summary>
    internal static bool IsUnder(string url, string baseUrl)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var page) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var root)) return false;
        if (page.GetLeftPart(UriPartial.Authority) != root.GetLeftPart(UriPartial.Authority)) return false;
        var path = root.AbsolutePath.TrimEnd('/');
        return page.AbsolutePath == path || page.AbsolutePath.StartsWith(path + "/", StringComparison.Ordinal);
    }

    public Task SaveStorageStateAsync(string path) => StorageState.SaveAsync(_context, path, _logger);

    public async ValueTask DisposeAsync()
    {
        if (KeepPageOpen) return;

        // Disposing an own context closes all its pages; a borrowed one keeps those the run did not open.
        if (_ownsContext) await CloseAsync(() => _context.DisposeAsync().AsTask());
        else foreach (var page in _pages) await CloseAsync(() => page.CloseAsync());
    }

    // A context of the run's own, unless IsolateContext: false shares the browser's default one; a saved login can
    // only seed a new context. An own context has the viewport from its creation, which saves every page a round trip.
    private static async Task<BrowserSession> OpenInAsync(IBrowser browser, PlaywrightFlowConfig config, ILogger logger)
    {
        var storageState = StorageState.FileToLoad(config, logger);
        if (!config.IsolateContext && storageState is null && browser.Contexts.Count > 0)
            return await StartAsync(browser.Contexts[0], ownsContext: false, config, logger);

        var options = new BrowserNewContextOptions
        {
            StorageStatePath = storageState,
            ViewportSize = new ViewportSize { Width = config.ViewportWidth, Height = config.ViewportHeight },
        };
        try
        {
            return await StartAsync(await browser.NewContextAsync(options), ownsContext: true, config, logger);
        }
        catch (PlaywrightException failure) when (failure.Message.Contains(ViewportCommand, StringComparison.Ordinal))
        {
            PageSetup.WarnNoViewport(config, logger, failure);
            options.ViewportSize = ViewportSize.NoViewport;
            return await StartAsync(await browser.NewContextAsync(options), ownsContext: true, config, logger);
        }
    }

    // Opens the run's page. If that fails, closes what it opened: the connection outlives the run, so nothing else would.
    private static async Task<BrowserSession> StartAsync(
        IBrowserContext context, bool ownsContext, PlaywrightFlowConfig config, ILogger logger)
    {
        var session = new BrowserSession(context, ownsContext, config, logger);
        try
        {
            // Before the page opens, so it and its pop-ups have it from their first script on. In a borrowed context
            // only pop-ups of the run's pages get it so; the page setup gives it to each page the run opens.
            if (ownsContext && config.EmulateDesktopPointer) await DesktopPointer.EmulateAsync(context);
            else if (config.EmulateDesktopPointer) await DesktopPointer.EmulateInPopupsAsync(context);
            session.Page = session.Track(await context.NewPageAsync());
            await session._setup.ApplyToRunPageAsync(session.Page);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    // A pop-up gets the run's settings as it opens, as a flow may act on it at once. Blocking its assets takes a
    // moment, so a pop-up that loads a page right away may load that page's assets.
    private IPage Track(IPage page)
    {
        _pages.Enqueue(page);
        page.Crash += (_, _) => _crashed[page] = true;
        page.Popup += (_, popup) => _ = SetUpPopupAsync(Track(popup));
        return page;
    }

    private async Task SetUpPopupAsync(IPage popup)
    {
        try
        {
            await _setup.ApplyAsync(popup);
        }
        catch (PlaywrightException) when (popup.IsClosed)
        {
            // It closed before it was set up.
        }
    }

    // A teardown error must not hide the flow's own failure.
    private async Task CloseAsync(Func<Task> close)
    {
        try
        {
            await close();
        }
        catch (Exception failure)
        {
            _logger.LogWarning("Could not close the page during teardown: {Message}", failure.Message);
        }
    }
}
