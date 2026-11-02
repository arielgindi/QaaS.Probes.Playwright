using System.Globalization;
using Microsoft.Playwright;
using QaaS.Playwright.Browser;

namespace QaaS.Playwright.Recorder.Capture;

/// <summary>How to open the browser a recording runs in.</summary>
internal sealed record RecordingOptions(string Url)
{
    /// <summary>The login to start from; the recorder's own saved login when it exists.</summary>
    public string? LoadStorageStatePath { get; init; } = File.Exists(BrowserDefaults.AuthStatePath)
        ? BrowserDefaults.AuthStatePath
        : null;

    /// <summary>Where to save the login when the recording ends; null saves nothing.</summary>
    public string? SaveStorageStatePath { get; init; } = BrowserDefaults.AuthStatePath;

    /// <summary>Extra Chrome flags, e.g. <c>--headless=new</c> for tests.</summary>
    public IReadOnlyList<string> BrowserArgs { get; init; } = [];
}

/// <summary>
/// A recording in progress: Chrome with the capture script in every page, and the log of what the person does. It ends
/// when the person closes the browser's last window, or when <see cref="StopAsync"/> is called (Enter in the console).
/// </summary>
internal sealed class RecordingSession : IAsyncDisposable
{
    private const string Binding = "__qaasRecord";
    private const string ExplainBinding = "__qaasExplain";
    private const float NavigationTimeoutMs = 60_000;
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(5);

    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;
    private readonly IBrowserContext _context;
    private readonly RecordingOptions _options;
    private readonly Action<object> _onNews;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<IPage, string> _aliases = [];
    private readonly Lock _gate = new();
    private Recording? _result;

    private RecordingSession(
        IPlaywright playwright, IBrowser browser, IBrowserContext context, RecordingOptions options, Action<object> onNews)
    {
        _playwright = playwright;
        _browser = browser;
        _context = context;
        _options = options;
        _onNews = onNews;
    }

    /// <summary>The page the recording started on; tests drive it.</summary>
    public IPage Page { get; private set; } = null!;

    public CaptureLog Log { get; } = new();

    /// <summary>Completes when the person closed the browser.</summary>
    public Task Closed => _closed.Task;

    /// <summary>Why the login could not be saved when the recording ended; null when it was, or nothing was to save.</summary>
    public string? LoginNotSaved { get; private set; }

    /// <summary>
    /// Opens Chrome on the URL with the capture script installed. <paramref name="onNews"/> receives each action and
    /// effect as it is captured, on Playwright's thread.
    /// </summary>
    public static async Task<RecordingSession> StartAsync(RecordingOptions options, Action<object> onNews)
    {
        var playwright = await StartPlaywrightAsync();
        try
        {
            playwright.Selectors.SetTestIdAttribute(BrowserDefaults.TestIdAttribute);
            var browser = await playwright.Chromium.LaunchAsync(new()
            {
                Channel = BrowserDefaults.ChromeChannel, Headless = false, Args = options.BrowserArgs,
                Timeout = NavigationTimeoutMs,
            });
            var context = await browser.NewContextAsync(new()
            {
                ViewportSize = Viewport(BrowserDefaults.RecorderViewport),
                StorageStatePath = options.LoadStorageStatePath,
            });
            var session = new RecordingSession(playwright, browser, context, options, onNews);
            await session.InstallAsync();
            return session;
        }
        catch
        {
            playwright.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Ends the recording: saves the login, closes the browser, and returns what was recorded. A login that cannot be
    /// saved is told in <see cref="LoginNotSaved"/>, and never costs the recording.
    /// </summary>
    public async Task<Recording> StopAsync()
    {
        lock (_gate)
            if (_result is not null)
                return _result;

        try
        {
            await Log.SettleAsync(SettleTimeout);
            var recording = Log.Build(_options.Url);
            await SaveLoginAsync();
            lock (_gate) return _result ??= recording;
        }
        finally
        {
            await DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _browser.CloseAsync();
        }
        catch (PlaywrightException)
        {
            // Already closed by the person.
        }

        _playwright.Dispose();
    }

    // The capture script locates elements with Playwright's console API, which the driver only installs in debug
    // mode "console". The variable is read once, when the driver starts, so only this driver gets it.
    private static async Task<IPlaywright> StartPlaywrightAsync()
    {
        const string debug = "PWDEBUG";
        var previous = Environment.GetEnvironmentVariable(debug);
        Environment.SetEnvironmentVariable(debug, "console");
        try
        {
            return await Microsoft.Playwright.Playwright.CreateAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable(debug, previous);
        }
    }

    private async Task InstallAsync()
    {
        await _context.ExposeBindingAsync(Binding, (BindingSource source, string json) =>
        {
            var news = Log.Add(AliasOf(source.Page), json);
            if (news is not null) _onNews(news);
        });
        await _context.ExposeBindingAsync(ExplainBinding, (BindingSource _, string json) => OverlayLabel.For(json));
        await _context.AddInitScriptAsync(CaptureScript.For(BrowserDefaults.TestIdAttribute));
        _context.Page += (_, page) =>
        {
            // The first page is the one the recording opens itself; any other is a pop-up or a tab.
            if (Track(page)) Log.PageOpened(AliasOf(page), Now());
        };
        _browser.Disconnected += (_, _) => _closed.TrySetResult();

        Page = await _context.NewPageAsync();
        Track(Page);
        await Page.GotoAsync(_options.Url, new() { Timeout = NavigationTimeoutMs });
    }

    // Returns whether the page was opened during the recording, rather than by it.
    private bool Track(IPage page)
    {
        bool opened;
        lock (_gate)
        {
            if (_aliases.ContainsKey(page)) return false;
            opened = _aliases.Count > 0;
            _aliases[page] = opened ? $"page{_aliases.Count}" : "page";
        }

        page.FrameNavigated += (_, frame) =>
        {
            if (frame == page.MainFrame) Log.Navigated(AliasOf(page), frame.Url, Now());
        };
        page.Request += (_, request) =>
        {
            if (request.IsNavigationRequest && request.RedirectedFrom is null && request.Frame == page.MainFrame)
                Log.NavigationRequested(AliasOf(page), StartedByThePersonAsync(request));
        };
        // A handler keeps a browser dialog open for the person to answer; without one Playwright dismisses it.
        page.Dialog += (_, dialog) => Log.BrowserDialog(AliasOf(page), dialog.Message, Now());
        page.Close += (_, _) =>
        {
            if (_context.Pages.All(open => open.IsClosed)) _closed.TrySetResult();
        };
        return opened;
    }

    private string AliasOf(IPage? page)
    {
        lock (_gate) return page is not null && _aliases.TryGetValue(page, out var alias) ? alias : "page";
    }

    // A document the person asked for themselves (the address bar, a bookmark) is requested with Sec-Fetch-Site: none;
    // one a link, a form or a script asked for says where it came from.
    private static async Task<bool> StartedByThePersonAsync(IRequest request)
    {
        try
        {
            return await request.HeaderValueAsync("sec-fetch-site") == "none";
        }
        catch (PlaywrightException)
        {
            return false; // The page closed first.
        }
    }

    // The login is kept even when the person closed the window; only a browser that is gone cannot give it.
    private async Task SaveLoginAsync()
    {
        if (_options.SaveStorageStatePath is not { } path || !_browser.IsConnected) return;
        try
        {
            await AtomicFileWriter.WriteAsync(path, await _context.StorageStateAsync());
        }
        catch (PlaywrightException)
        {
            // The browser closed while saving: the previous login stays.
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            LoginNotSaved = $"The login was not saved to {Path.GetFullPath(path)}: {failure.Message}";
        }
    }

    private static ViewportSize Viewport(string widthComma) =>
        widthComma.Split(',') is [var width, var height]
            ? new ViewportSize
            {
                Width = int.Parse(width.Trim(), CultureInfo.InvariantCulture),
                Height = int.Parse(height.Trim(), CultureInfo.InvariantCulture),
            }
            : throw new InvalidOperationException($"RecorderViewport '{widthComma}' is not W,H.");

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
