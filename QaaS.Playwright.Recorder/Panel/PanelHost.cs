using Microsoft.Playwright;

namespace QaaS.Playwright.Recorder.Panel;

/// <summary>
/// The recorder's window: a page of its own Chrome, in a browser context of its own, so nothing done in it is ever
/// recorded, and it needs no server or port. Its page is embedded in the tool and loaded once; the screen it shows
/// talks to the recorder through one exposed function, which goes to whoever the window shows now: the start screen,
/// then a recording's panel, then the start screen's Done.
/// </summary>
internal sealed class PanelHost : IAsyncDisposable
{
    private const string Resource = "QaaS.Playwright.Recorder.Panel.panel.html";
    private const string SendFunction = "__qaasPanelSend";
    private static readonly Lazy<string> Html = new(LoadHtml);

    private readonly IBrowserContext _context;
    private readonly bool _owned;
    private volatile Action<string>? _receiver;
    private int _disposed;

    private PanelHost(IBrowserContext context, IPage page, bool owned)
    {
        (_context, Page, _owned) = (context, page, owned);
        page.Close += (_, _) => _closed.TrySetResult();
        if (context.Browser is { } browser) browser.Disconnected += (_, _) => _closed.TrySetResult();
    }

    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The window's page.</summary>
    public IPage Page { get; }

    /// <summary>What the person chose in the window, kept between runs.</summary>
    public PanelPrefs Prefs { get; } = PanelPrefs.Load();

    /// <summary>Completes once the person closed the window, or Chrome went away.</summary>
    public Task Closed => _closed.Task;

    /// <summary>
    /// Opens the window in <paramref name="browser"/>, its page loaded. A window the recorder opens for a recording alone
    /// (<paramref name="owned"/> false), as a test does, is its panel and nothing else.
    /// </summary>
    /// <exception cref="PlaywrightException">The browser could not open it.</exception>
    public static async Task<PanelHost> OpenAsync(IBrowser browser, bool owned = true)
    {
        // No viewport, so the page fills its window; no color scheme, so it follows the system's.
        var context = await browser.NewContextAsync(new() { ViewportSize = ViewportSize.NoViewport, ColorScheme = ColorScheme.Null });
        try
        {
            var page = await context.NewPageAsync();
            var host = new PanelHost(context, page, owned);
            await page.ExposeFunctionAsync<string>(SendFunction, json => host._receiver?.Invoke(json));
            await host.LoadAsync();
            return host;
        }
        catch
        {
            await context.CloseAsync();
            throw;
        }
    }

    /// <summary>What the page sends goes to <paramref name="receiver"/> from now on; null, nowhere.</summary>
    public void Route(Action<string>? receiver) => _receiver = receiver;

    /// <summary>Loads the page anew, the choices kept in it, e.g. once it lost its state.</summary>
    public Task LoadAsync() => Page.SetContentAsync(Prefs.Into(Html.Value));

    /// <summary>Calls <paramref name="function"/> of the page with <paramref name="json"/>; false when the page has it not.</summary>
    public async Task<bool> CallAsync(string function, string json)
    {
        try
        {
            return await Page.EvaluateAsync<bool>($"json => typeof window.{function} === 'function' && window.{function}(json)", json);
        }
        catch (PlaywrightException) when (Page.IsClosed)
        {
            return false;
        }
    }

    /// <summary>Closes the window, unless it was opened for a recording alone, which closes with its browser.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _receiver = null;
        if (!_owned) return;
        try
        {
            await _context.CloseAsync();
        }
        catch (PlaywrightException)
        {
            // The browser is gone already.
        }
    }

    private static string LoadHtml()
    {
        using var stream = typeof(PanelHost).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"The embedded {Resource} is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
