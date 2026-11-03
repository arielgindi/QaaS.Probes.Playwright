using System.Globalization;
using System.Text.Json;
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

    /// <summary>
    /// The login flow that runs first, by its class name: nothing is recorded until <see cref="RecordingSession.Resume"/>,
    /// and the recording starts on the page the login left. Null to record at once.
    /// </summary>
    public string? LoginFlow { get; init; }
}

/// <summary>Whether what the person does is recorded.</summary>
internal enum RecordingState
{
    Recording,

    /// <summary>The person paused the recording: nothing is recorded until they resume it.</summary>
    Paused,

    /// <summary>A login flow is running: nothing it does is recorded.</summary>
    LoggingIn,
}

/// <summary>
/// A recording in progress: Chrome with the capture script in every page, and the log of what the person does. It ends
/// when the person closes the browser's last window, or when <see cref="StopAsync"/> is called (Enter in the console).
/// The person can pause it (F8, or the panel), and freeze the page's elements for the panel (F4).
/// </summary>
internal sealed class RecordingSession : IAsyncDisposable
{
    private const string Binding = "__qaasRecord";
    private const string ExplainBinding = "__qaasExplain";
    private const string ControlBinding = "__qaasControl";
    private const float NavigationTimeoutMs = 60_000;
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DevToolsPortTimeout = TimeSpan.FromSeconds(10);

    private readonly IPlaywright _playwright;
    private IPlaywright? _consoleApi;
    private readonly IBrowser _browser;
    private readonly IBrowserContext _context;
    private readonly RecordingOptions _options;
    private readonly Action<object> _onNews;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<IPage, string> _aliases = [];
    private readonly HashSet<IPage> _watchingDialogs = [];
    private readonly Lock _gate = new();
    private Recording? _result;
    private RecordingState _state;

    private RecordingSession(IPlaywright playwright, IBrowser browser, IBrowserContext context, RecordingOptions options,
        Action<object> onNews)
    {
        _playwright = playwright;
        _browser = browser;
        _context = context;
        _options = options;
        _onNews = onNews;
        _state = options.LoginFlow is null ? RecordingState.Recording : RecordingState.LoggingIn;
        Log = new CaptureLog(paused: options.LoginFlow is not null);
    }

    /// <summary>The page the recording started on; tests drive it, and a login flow runs on it.</summary>
    public IPage Page { get; private set; } = null!;

    public CaptureLog Log { get; }

    /// <summary>Completes when the person closed the browser.</summary>
    public Task Closed => _closed.Task;

    /// <summary>Why the login could not be saved when the recording ended; null when it was, or nothing was to save.</summary>
    public string? LoginNotSaved { get; private set; }

    public RecordingState State
    {
        get { lock (_gate) return _state; }
    }

    /// <summary>Why the recording is paused when the person did not pause it, e.g. the login flow failed; else null.</summary>
    public string? Notice { get; private set; }

    /// <summary>The page's elements as they were when the person last pressed F4, for the panel; null before.</summary>
    public FrozenElements? Frozen { get; private set; }

    /// <summary>Raised, on Playwright's thread, when a page's elements changed or were frozen.</summary>
    public event Action? ElementsChanged;

    /// <summary>
    /// Opens Chrome on the URL with the capture script installed. <paramref name="onNews"/> receives each action and
    /// effect as it is captured, and each <see cref="RecordingState"/> the recording goes to, on Playwright's thread.
    /// </summary>
    public static async Task<RecordingSession> StartAsync(RecordingOptions options, Action<object> onNews)
    {
        var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        RecordingSession? session = null;
        try
        {
            playwright.Selectors.SetTestIdAttribute(BrowserDefaults.TestIdAttribute);
            var browser = await playwright.Chromium.LaunchAsync(new()
            {
                Channel = BrowserDefaults.ChromeChannel, Headless = false, Timeout = NavigationTimeoutMs,
                // Port 0: Chrome takes a free one, on this machine only, for the console API's driver below.
                Args = [.. options.BrowserArgs, "--remote-debugging-port=0"],
            });
            var context = await browser.NewContextAsync(new()
            {
                ViewportSize = Viewport(BrowserDefaults.RecorderViewport),
                StorageStatePath = options.LoadStorageStatePath,
            });
            session = new RecordingSession(playwright, browser, context, options, onNews);
            await session.InstallAsync();
            // After a login flow, which then runs alone with the recording's driver, as with the probe's.
            if (options.LoginFlow is null) await session.AttachConsoleApiAsync();
            return session;
        }
        catch
        {
            session?._consoleApi?.Dispose();
            playwright.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Runs the login flow on <see cref="Page"/>, which <paramref name="logIn"/> does and returns why it failed, or null.
    /// The recording then starts on the page it left, or stays paused with why, for the person to log in by hand.
    /// </summary>
    public async Task<string?> LogInAsync(Func<IPage, Task<string?>> logIn)
    {
        var failure = await logIn(Page);
        await AttachConsoleApiAsync();
        if (failure is null) Resume();
        else Pause($"{_options.LoginFlow} failed: {failure}");
        return failure;
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
            var recording = Build();
            await SaveLoginAsync();
            lock (_gate) return _result ??= recording;
        }
        finally
        {
            await DisposeAsync();
        }
    }

    /// <summary>The recording so far, as <see cref="StopAsync"/> would return it.</summary>
    public Recording Build() => Log.Build(_options.Url) with { After = _options.LoginFlow };

    /// <summary>Pauses a recording that records, and resumes one that is paused; returns the state it went to.</summary>
    public RecordingState TogglePause() => State switch
    {
        RecordingState.Recording => Pause(),
        RecordingState.Paused => Resume(),
        var loggingIn => loggingIn,
    };

    /// <summary>
    /// Stops recording until <see cref="Resume"/>; <paramref name="notice"/> tells the person why when they did not
    /// pause it themselves, e.g. the login flow failed.
    /// </summary>
    public RecordingState Pause(string? notice = null)
    {
        lock (_gate)
        {
            if (_state == RecordingState.Paused && notice is null) return _state;
            Log.Pause(Now());
            (_state, Notice) = (RecordingState.Paused, notice);
            WatchDialogs();
        }

        return Announce(RecordingState.Paused);
    }

    /// <summary>
    /// Records again, from the page the app is on now. After a pause, a page the app went to meanwhile is opened by its
    /// address, so the next step runs where the person left it; after the login flow, the recording starts where it
    /// left the page.
    /// </summary>
    public RecordingState Resume()
    {
        lock (_gate)
        {
            if (_state == RecordingState.Recording) return _state;
            var open = _aliases.Where(pair => !pair.Key.IsClosed).Select(pair => (pair.Value, pair.Key.Url)).ToList();
            Log.Resume(open, Now());
            (_state, Notice) = (RecordingState.Recording, null);
            WatchDialogs();
        }

        return Announce(RecordingState.Recording);
    }

    /// <summary>The open page of the recording named <paramref name="alias"/> (<c>page</c>, <c>page1</c>, ...), or null.</summary>
    public IPage? PageNamed(string alias)
    {
        lock (_gate) return _aliases.FirstOrDefault(pair => pair.Value == alias && !pair.Key.IsClosed).Key;
    }

    /// <summary>The alias of the open page the person last acted on, else of the first page.</summary>
    public string CurrentPage()
    {
        var last = Log.LastPage();
        return last is not null && PageNamed(last) is not null ? last : "page";
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

        _consoleApi?.Dispose();
        _playwright.Dispose();
    }

    // The capture script locates elements with Playwright's console API, which a driver only installs in debug mode
    // "console", read once, when the driver starts. Debug mode also turns off every timeout, so the recording's own driver
    // is not in it: a second driver is, and attaches to the same Chrome over CDP only to install the console API in its
    // pages, which it does in every page of every context it sees, those open now included. It also answers every call of
    // an exposed function there that it does not know, with an error, so the capture script never waits for an answer:
    // the recorder gives it by evaluating in the page.
    private async Task AttachConsoleApiAsync()
    {
        if (_consoleApi is not null) return;
        var endpoint = await DevToolsEndpointAsync(_browser);
        const string debug = "PWDEBUG";
        var previous = Environment.GetEnvironmentVariable(debug);
        Environment.SetEnvironmentVariable(debug, "console");
        IPlaywright consoleApi;
        try
        {
            consoleApi = await Microsoft.Playwright.Playwright.CreateAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable(debug, previous);
        }

        try
        {
            consoleApi.Selectors.SetTestIdAttribute(BrowserDefaults.TestIdAttribute);
            await consoleApi.Chromium.ConnectOverCDPAsync(endpoint, new() { Timeout = NavigationTimeoutMs });
            _consoleApi = consoleApi;
        }
        catch
        {
            consoleApi.Dispose();
            throw;
        }
    }

    // Chrome writes the port it took, and the browser's path, to DevToolsActivePort in its profile folder, which
    // Playwright chose and the browser's command line tells.
    private static async Task<string> DevToolsEndpointAsync(IBrowser browser)
    {
        const string profileFlag = "--user-data-dir=";
        var devTools = await browser.NewBrowserCDPSessionAsync();
        var commandLine = await devTools.SendAsync("Browser.getBrowserCommandLine");
        await devTools.DetachAsync();
        var profile = commandLine?.GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString())
                          .FirstOrDefault(argument => argument?.StartsWith(profileFlag, StringComparison.Ordinal) == true)?[profileFlag.Length..]
                      ?? throw new PlaywrightException("Chrome did not tell its profile folder.");
        var portFile = Path.Combine(profile, "DevToolsActivePort");
        var waited = TimeSpan.Zero;
        for (; waited < DevToolsPortTimeout; waited += TimeSpan.FromMilliseconds(50))
        {
            try
            {
                if (File.Exists(portFile) && await File.ReadAllLinesAsync(portFile) is [var port, var path, ..])
                    return $"ws://127.0.0.1:{port}{path}";
            }
            catch (IOException)
            {
                // Chrome is writing it.
            }

            await Task.Delay(50);
        }

        throw new PlaywrightException("Chrome did not open its DevTools port.");
    }

    private async Task InstallAsync()
    {
        await _context.ExposeBindingAsync(Binding, (BindingSource source, string json) =>
        {
            var news = Log.Add(AliasOf(source.Page), json);
            if (news is not null) _onNews(news);
        });
        await _context.ExposeBindingAsync(ExplainBinding, (BindingSource source, string json) =>
        {
            if (source.Page is { } page) _ = PageElements.CallAsync(page, OverlayLabel.Reply(json));
        });
        await _context.ExposeBindingAsync(ControlBinding, (BindingSource source, string json) => Control(source.Page, json));
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

    // What the capture script asks or tells: the state its overlay shows, F8 to pause or resume, F4 with the frozen
    // elements, or that the page's elements changed.
    private void Control(IPage? page, string json)
    {
        var message = JsonSerializer.Deserialize<ControlMessage>(json, Recording.Json);
        switch (message?.Type)
        {
            case "state" when page is not null:
                _ = PageElements.CallAsync(page, new { op = "state", state = StateName(State) });
                break;
            case "pause":
                TogglePause();
                break;
            case "freeze" when message.Tree is { } tree:
                Frozen = new FrozenElements(AliasOf(page), DateTimeOffset.Now, tree.GetRawText(), message.Described ?? []);
                _onNews(Frozen);
                ElementsChanged?.Invoke();
                break;
            case "changed":
                ElementsChanged?.Invoke();
                break;
        }
    }

    private RecordingState Announce(RecordingState state)
    {
        _onNews(state);
        _ = ShowStateAsync(state);
        return state;
    }

    // Each page's overlay shows the state: paused, or nothing while the login flow runs. A page loading meanwhile asks.
    private async Task ShowStateAsync(RecordingState state)
    {
        List<IPage> pages;
        lock (_gate) pages = [.. _aliases.Keys.Where(page => !page.IsClosed)];
        foreach (var page in pages) await PageElements.CallAsync(page, new { op = "state", state = StateName(state) });
    }

    private static string StateName(RecordingState state) => state switch
    {
        RecordingState.Paused => "paused",
        RecordingState.LoggingIn => "login",
        _ => "recording",
    };

    // Returns whether the page was opened during the recording, rather than by it.
    private bool Track(IPage page)
    {
        bool opened;
        lock (_gate)
        {
            if (_aliases.ContainsKey(page)) return false;
            opened = _aliases.Count > 0;
            _aliases[page] = opened ? $"page{_aliases.Count}" : "page";
            if (_state != RecordingState.LoggingIn) WatchDialogs(page);
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
        page.Close += (_, _) =>
        {
            if (_context.Pages.All(open => open.IsClosed)) _closed.TrySetResult();
        };
        return opened;
    }

    // A handler keeps a browser dialog open for the person to answer. None is added while the login flow runs, so
    // Playwright dismisses a dialog the flow does not handle itself, as in the probe.
    private void WatchDialogs()
    {
        foreach (var page in _aliases.Keys) WatchDialogs(page);
    }

    private void WatchDialogs(IPage page)
    {
        if (!_watchingDialogs.Add(page)) return;
        page.Dialog += (_, dialog) => Log.BrowserDialog(AliasOf(page), dialog.Message, Now());
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

    private sealed class ControlMessage
    {
        public string Type { get; set; } = "";

        /// <summary>For F4: the page's elements, as the panel shows them.</summary>
        public JsonElement? Tree { get; set; }

        /// <summary>For F4: what the capture script found to locate each element a click may target, by node id.</summary>
        public Dictionary<int, ElementDescription>? Described { get; set; }
    }
}
