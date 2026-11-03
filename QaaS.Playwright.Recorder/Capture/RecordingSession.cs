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

    /// <summary>
    /// A recording this one goes on from: its actions come first, and are replayed after the login flow, as in a test
    /// (<see cref="RecordingState.Testing"/>), before anything is recorded. Null to record anew.
    /// </summary>
    public Recording? Continues { get; init; }
}

/// <summary>How a login flow ended when it did not log in: why, and whether it was stopped for the person to log in by hand.</summary>
/// <param name="Why">Why it failed, as the probe says it; or why it was stopped.</param>
/// <param name="ByHand">It was stopped (Esc, the panel, or it hung), rather than failing.</param>
internal sealed record LoginEnd(string Why, bool ByHand);

/// <summary>Whether what the person does is recorded.</summary>
internal enum RecordingState
{
    Recording,

    /// <summary>The person paused the recording: nothing is recorded until they resume it.</summary>
    Paused,

    /// <summary>A login flow is running: nothing it does is recorded.</summary>
    LoggingIn,

    /// <summary>The flow is being tested in a window of its own: nothing is recorded until the test ends.</summary>
    Testing,

    /// <summary>
    /// The person tries things in the app (F7, or Try in the panel): nothing is recorded, as when paused, but each action
    /// is told, as <see cref="RecordingSession.Tried"/>, with what the recorder would write for it.
    /// </summary>
    Trying,
}

/// <summary>
/// A recording in progress: Chrome with the capture script in every page, and the log of what the person does. It ends
/// when the person closes the browser's last window, or when <see cref="StopAsync"/> is called (Enter in the console).
/// The person can pause it (F8, or the panel), freeze the page's elements for the panel (F4), choose checks of the page
/// (F6), try things without recording them (F7), and test the flow so far, or replay it up to a part they record again.
/// </summary>
internal sealed class RecordingSession : IAsyncDisposable
{
    private const string Binding = "__qaasRecord";
    private const string ExplainBinding = "__qaasExplain";
    private const string ControlBinding = "__qaasControl";
    private const float NavigationTimeoutMs = 60_000;
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DevToolsPortTimeout = TimeSpan.FromSeconds(10);

    // A driver reads PWDEBUG from the process as it starts: sessions that start one at the same time take turns, so none
    // starts with the console API's value, which turns off every timeout, nor leaves it set for the drivers after it.
    private static readonly SemaphoreSlim DriverStart = new(1, 1);

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
    private RecordingState _beforeTest;
    private volatile LoginWatch? _login;
    private RecordingState _beforeTry;
    private string? _rerecording;

    private RecordingSession(IPlaywright playwright, IBrowser browser, IBrowserContext context, RecordingOptions options,
        Action<object> onNews)
    {
        _playwright = playwright;
        _browser = browser;
        _context = context;
        _options = options;
        _onNews = onNews;
        _state = options.LoginFlow is not null ? RecordingState.LoggingIn
            : options.Continues is not null ? RecordingState.Testing
            : RecordingState.Recording;
        _beforeTest = RecordingState.Recording;
        Log = new CaptureLog(paused: _state != RecordingState.Recording, options.Continues);
    }

    /// <summary>The page the recording started on; tests drive it, and a login flow runs on it.</summary>
    public IPage Page { get; private set; } = null!;

    public CaptureLog Log { get; }

    /// <summary>How the browser was opened: the URL, and the login it started from.</summary>
    public RecordingOptions Options => _options;

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
    /// Raised, on Playwright's thread, with what a page in inspect mode (Ctrl+Shift+C) tells, as JSON: that it went in or
    /// out of it, the element under the pointer, or the one a click picked, with the elements around it.
    /// </summary>
    public event Action<string, string>? Inspected;

    /// <summary>
    /// A page is in check mode (F6, or Check in the panel): a click there picks an element to check, and does nothing
    /// else.
    /// </summary>
    public bool Checking { get; private set; }

    /// <summary>Raised, on Playwright's thread, when a page went in or out of check mode.</summary>
    public event Action? CheckingChanged;

    /// <summary>How the login flow goes while it runs, each second; null when none runs.</summary>
    public LoginProgress? LoginProgress { get; private set; }

    /// <summary>Raised each second while the login flow runs, and when it ended.</summary>
    public event Action? LoginChanged;

    /// <summary>
    /// While trying (<see cref="RecordingState.Trying"/>), what the person did, collected as the recording is, but apart
    /// from it; else null.
    /// </summary>
    public CaptureLog? Tried { get; private set; }

    /// <summary>
    /// What the overlay says of the part being recorded again, e.g. <c>Re-recording FillNewTicketAsync</c>
    /// (<see cref="Rerecording"/>); else null.
    /// </summary>
    public string? RerecordingPart
    {
        get { lock (_gate) return _rerecording; }
    }

    /// <summary>Raised, on Playwright's thread, when the person pressed F2 in the app while recording a part again.</summary>
    public event Action? PartDone;

    /// <summary>
    /// Opens Chrome on the URL with the capture script installed. <paramref name="onNews"/> receives each action and
    /// effect as it is captured, and each <see cref="RecordingState"/> the recording goes to, on Playwright's thread.
    /// </summary>
    public static async Task<RecordingSession> StartAsync(RecordingOptions options, Action<object> onNews)
    {
        IPlaywright playwright;
        await DriverStart.WaitAsync();
        try
        {
            playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        }
        finally
        {
            DriverStart.Release();
        }

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
    /// Runs the login flow on <see cref="Page"/>, which <paramref name="logIn"/> does and returns why it failed, or null;
    /// its token is cancelled once it was stopped. While it runs, <see cref="LoginProgress"/> says each second how it
    /// goes, and the pages' overlay shows it. <see cref="LogInByHand"/> stops it, as it stops itself after two minutes
    /// of nothing, or when the app asked the browser to sign in: its page is then replaced by a fresh one at the same
    /// address, the login so far kept, so whatever the flow waited for fails at once, and nothing it does reaches the
    /// app. The recording then starts on the page the login left, or stays paused with why, for the person to log in
    /// by hand. A recording that continues another goes on to replay it (<see cref="EndTest"/> ends that).
    /// </summary>
    /// <param name="logIn">Runs the flow on the page.</param>
    /// <param name="scrub">Hides the login's secrets in what is shown of it, e.g. a page's address.</param>
    public async Task<LoginEnd?> LogInAsync(Func<IPage, CancellationToken, Task<string?>> logIn, Func<string, string>? scrub = null)
    {
        var flow = _options.LoginFlow ?? "the login flow";
        var watch = new LoginWatch(flow, _options.Url, scrub ?? (text => text));
        watch.Navigated(Page.Url);
        _login = watch;
        WatchNetwork(on: true);
        using var stopped = new CancellationTokenSource();
        LoginEnd? end;
        try
        {
            var running = logIn(Page, stopped.Token);
            using var ticking = new CancellationTokenSource();
            var ticker = TickAsync(watch, ticking.Token);
            var first = await Task.WhenAny((Task)running, watch.ByHand);
            await ticking.CancelAsync();
            await ticker;
            if (first == running)
            {
                end = await running is { } failure ? new LoginEnd(failure, ByHand: false) : null;
            }
            else
            {
                await stopped.CancelAsync();
                end = new LoginEnd(await watch.ByHand, ByHand: true);
                await ReplacePageAsync();
            }
        }
        finally
        {
            _login = null;
            WatchNetwork(on: false);
            LoginProgress = null;
        }

        await AttachConsoleApiAsync();
        if (end is not null) Pause(end.ByHand ? end.Why : $"{flow} failed: {end.Why}");
        else if (_options.Continues is null) Resume();
        else
        {
            lock (_gate) _state = RecordingState.Testing;
            Announce(RecordingState.Testing);
        }

        // The panel shows the state it went to.
        LoginChanged?.Invoke();
        return end;
    }

    /// <summary>Stops the login flow running, for the person to log in by hand: Esc, or the panel's Log in by hand.</summary>
    public void LogInByHand()
    {
        if (_login is { } login) login.Stop($"You stopped {login.Flow}.");
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

    /// <summary>
    /// Pauses a recording that records, and resumes one that is paused; returns the state it went to. While the login
    /// flow runs, the flow is tested, or the person tries things, it stays as it is.
    /// </summary>
    public RecordingState TogglePause() => State switch
    {
        RecordingState.Recording => Pause(),
        RecordingState.Paused => Resume(),
        var other => other,
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

    /// <summary>
    /// Stops recording while the flow is tested in a window of its own, as a pause does, so nothing the person does in
    /// the app meanwhile is recorded either. Returns false while the login flow runs, or a test already does.
    /// </summary>
    public bool BeginTest()
    {
        lock (_gate)
        {
            if (_state is RecordingState.LoggingIn or RecordingState.Testing) return false;
            _beforeTest = _state;
            Log.Pause(Now());
            _state = RecordingState.Testing;
        }

        Announce(RecordingState.Testing);
        return true;
    }

    /// <summary>
    /// Goes back to what the recording did before the test: records again, as after a pause, or stays paused; or, with
    /// <paramref name="paused"/>, stays paused anyway, as after a replay that left the app elsewhere.
    /// </summary>
    public RecordingState EndTest(bool paused = false)
    {
        RecordingState state;
        lock (_gate)
        {
            if (_state != RecordingState.Testing) return _state;
            var to = paused && _beforeTest != RecordingState.Trying ? RecordingState.Paused : _beforeTest;
            if (to == RecordingState.Recording) Log.Resume(OpenPages(), Now());
            state = _state = to;
        }

        return Announce(state);
    }

    /// <summary>
    /// After a replay up to a part of the flow, which the person records again (<see cref="CaptureLog.BeginSplice"/>):
    /// records from the pages as the replay left them, the overlay saying <paramref name="part"/>, until
    /// <see cref="Rerecording"/> is given null. The pages went where the flow goes, so nothing is gone to by its address.
    /// </summary>
    public RecordingState ResumeAfterReplay(string part)
    {
        lock (_gate)
        {
            if (_state != RecordingState.Testing) return _state;
            Log.Resume(OpenPages(), Now(), rebase: true);
            (_state, Notice, _rerecording) = (RecordingState.Recording, null, part);
            WatchDialogs();
        }

        return Announce(RecordingState.Recording);
    }

    /// <summary>Says in the overlay what part the person records again, and how to end it; null, none.</summary>
    public void Rerecording(string? part)
    {
        lock (_gate) _rerecording = part;
        _ = ShowStateAsync(State);
    }

    /// <summary>
    /// Has the person try things in the app, or go back to what the recording did before (F7, or Try in the panel):
    /// while trying, nothing is recorded, as when paused, and what they do is collected in <see cref="Tried"/> instead.
    /// Returns the state it went to; while the login flow runs or the flow is tested, it stays as it is. Not
    /// <paramref name="resume"/>, it goes from trying to paused, e.g. before the flow is replayed up to a part.
    /// </summary>
    public RecordingState ToggleTry(bool resume = true)
    {
        RecordingState state;
        lock (_gate)
        {
            switch (_state)
            {
                case RecordingState.Recording or RecordingState.Paused:
                    _beforeTry = _state;
                    Log.Pause(Now());
                    var tried = new CaptureLog(paused: true);
                    // From the pages as they are: where they go from here is what the person tried.
                    tried.Resume(OpenPages(), Now());
                    (Tried, _state) = (tried, RecordingState.Trying);
                    break;
                case RecordingState.Trying:
                    var back = resume ? _beforeTry : RecordingState.Paused;
                    if (back == RecordingState.Recording) Log.Resume(OpenPages(), Now());
                    (Tried, _state) = (null, back);
                    break;
                default:
                    return _state;
            }

            state = _state;
            WatchDialogs();
        }

        return Announce(state);
    }

    /// <summary>
    /// Brings the recorded page back to how the recording started, before it is replayed from there: its cookies and the
    /// start page's storage cleared, as a login flow logs in afresh, or a recording that starts by logging in
    /// (<see cref="Recording.LogsIn"/>), else as the login the recording started from.
    /// </summary>
    public async Task ResetToStartAsync()
    {
        await _context.ClearCookiesAsync();
        var start = new Uri(_options.Url).GetLeftPart(UriPartial.Authority);
        var saved = _options.LoginFlow is null && !Build().LogsIn ? await SavedLoginAsync() : null;
        var origins = saved?.Origins.Select(origin => origin.Origin).Append(start).Distinct() ?? [start];
        var devTools = await _context.NewCDPSessionAsync(Page);
        try
        {
            foreach (var origin in origins)
                await devTools.SendAsync("Storage.clearDataForOrigin", new()
                {
                    ["origin"] = origin, ["storageTypes"] = "local_storage,session_storage,indexeddb,cache_storage,service_workers",
                });
        }
        finally
        {
            await devTools.DetachAsync();
        }

        if (saved is null) return;
        if (saved.Cookies.Count > 0)
            await _context.AddCookiesAsync(saved.Cookies.Select(cookie => new Cookie
            {
                Name = cookie.Name, Value = cookie.Value, Domain = cookie.Domain, Path = cookie.Path, Expires = cookie.Expires,
                HttpOnly = cookie.HttpOnly, Secure = cookie.Secure,
                SameSite = Enum.TryParse<SameSiteAttribute>(cookie.SameSite, ignoreCase: true, out var sameSite) ? sameSite : null,
            }));
        // Each origin's storage is put back on an empty page of that origin, where none of the app's code runs.
        foreach (var origin in saved.Origins.Where(origin => origin.LocalStorage.Count > 0))
        {
            var blank = $"{origin.Origin}/__qaas-recorder-blank";
            await Page.RouteAsync(blank, route => route.FulfillAsync(new() { ContentType = "text/html", Body = "" }));
            try
            {
                await Page.GotoAsync(blank);
                await Page.EvaluateAsync("items => { for (const { name, value } of items) localStorage.setItem(name, value); }",
                    origin.LocalStorage.Select(item => new { name = item.Name, value = item.Value }).ToArray());
            }
            finally
            {
                await Page.UnrouteAsync(blank);
            }
        }
    }

    /// <summary>
    /// Has the page the person last acted on go in or out of check mode, as F6 there does, and brings it to the front.
    /// The page says when it did: it refuses while the recording is paused.
    /// </summary>
    public async Task CheckModeAsync(bool on)
    {
        var page = PageNamed(CurrentPage());
        if (page is null) return;
        if (on) await page.BringToFrontAsync();
        await PageElements.CallAsync(page, new { op = "check", on });
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

    // Each second: how the login goes, for the console and the panel, and in each page's overlay; once it is slow, how
    // to stop it there.
    private async Task TickAsync(LoginWatch watch, CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do
            {
                var progress = LoginProgress = watch.Tick();
                _onNews(progress);
                LoginChanged?.Invoke();
                List<IPage> pages;
                lock (_gate) pages = [.. _aliases.Keys.Where(page => !page.IsClosed)];
                var hint = string.Join(" · ", new[] { progress.Where is null ? null : $"on {progress.Where}",
                    progress.Slow ? "Esc logs in by hand" : null }.OfType<string>());
                var shown = new { op = "login", text = $"Logging in with {watch.Flow}… {progress.Seconds} s", hint, esc = progress.Slow };
                // A page that is loading answers once it loaded: the next second will do.
                await Task.WhenAny(Task.WhenAll(pages.Select(page => PageElements.CallAsync(page, shown))), Task.Delay(500, stop));
            }
            while (await timer.WaitForNextTickAsync(stop));
        }
        catch (OperationCanceledException)
        {
            // The login ended.
        }
    }

    // What the login's pages send and get, while it runs: activity, what a document waits for, and the app asking the
    // browser to sign in. Only then, as Playwright sends every request's events while anyone listens.
    private void WatchNetwork(bool on)
    {
        if (on)
        {
            _context.Request += OnRequest;
            _context.RequestFinished += OnAnswered;
            _context.RequestFailed += OnAnswered;
            _context.Response += OnResponse;
        }
        else
        {
            _context.Request -= OnRequest;
            _context.RequestFinished -= OnAnswered;
            _context.RequestFailed -= OnAnswered;
            _context.Response -= OnResponse;
        }
    }

    private void OnRequest(object? sender, IRequest request) =>
        _login?.Requested(request, request.Url, request.IsNavigationRequest && request.Frame.ParentFrame is null);

    private void OnAnswered(object? sender, IRequest request) => _login?.Answered(request);

    private void OnResponse(object? sender, IResponse response)
    {
        if (response.Status == 401 && response.Request.IsNavigationRequest && response.Frame.ParentFrame is null
            && response.Headers.TryGetValue("www-authenticate", out var scheme))
            _login?.AuthAsked(scheme, response.Url);
    }

    // A fresh page in the old one's place, at the address it was at, and the old one closed: the login flow stopped
    // there fails at once, whatever it waited for, and does nothing more. The login so far is the context's, and stays.
    private async Task ReplacePageAsync()
    {
        var old = Page;
        var url = old.IsClosed || old.Url is "" or "about:blank" ? _options.Url : old.Url;
        var fresh = await _context.NewPageAsync();
        Track(fresh, "page");
        lock (_gate) _aliases.Remove(old);
        Page = fresh;
        try
        {
            await old.CloseAsync(new() { RunBeforeUnload = false });
            await fresh.GotoAsync(url, new() { Timeout = NavigationTimeoutMs });
        }
        catch (PlaywrightException)
        {
            // The person goes on from where it is.
        }
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
        IPlaywright consoleApi;
        await DriverStart.WaitAsync();
        var previous = Environment.GetEnvironmentVariable(debug);
        try
        {
            Environment.SetEnvironmentVariable(debug, "console");
            consoleApi = await Microsoft.Playwright.Playwright.CreateAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable(debug, previous);
            DriverStart.Release();
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
            var page = AliasOf(source.Page);
            var news = Log.Add(page, json);
            if (news is not null) _onNews(news);
            // What the person tries is told apart: the recording took only what it began before.
            else Tried?.Add(page, json);
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
            if (!Track(page)) return;
            Log.PageOpened(AliasOf(page), Now());
            Tried?.PageOpened(AliasOf(page), Now());
        };
        _browser.Disconnected += (_, _) => _closed.TrySetResult();

        Page = await _context.NewPageAsync();
        Track(Page);
        await Page.GotoAsync(_options.Url, new() { Timeout = NavigationTimeoutMs });
    }

    // What the capture script asks or tells: the state its overlay shows, F8 to pause or resume, F4 with the frozen
    // elements, that the page's elements changed, what it points at or picked in inspect mode, or that it went in or out
    // of check mode.
    private void Control(IPage? page, string json)
    {
        var message = JsonSerializer.Deserialize<ControlMessage>(json, Recording.Json);
        switch (message?.Type)
        {
            case "state" when page is not null:
                _ = PageElements.CallAsync(page, new { op = "state", state = StateName(State), part = RerecordingPart });
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
            case "inspect":
                Inspected?.Invoke(AliasOf(page), json);
                break;
            case "checking":
                Checking = message.On == true;
                CheckingChanged?.Invoke();
                break;
            case "loginByHand":
                LogInByHand();
                break;
            case "loginAction" when _login is { } login:
                var name = message.Name is { Length: > 0 } named ? $" “{named}”" : "";
                login.Acted(message.Action == "type"
                    ? $"typed in{(name.Length > 0 ? name : " a field")}"
                    : $"clicked{(message.Role is { Length: > 0 } role ? $" {role}" : "")}{name}");
                break;
            case "try":
                ToggleTry();
                break;
            case "done" when RerecordingPart is not null:
                PartDone?.Invoke();
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
        var part = RerecordingPart;
        foreach (var page in pages) await PageElements.CallAsync(page, new { op = "state", state = StateName(state), part });
    }

    private static string StateName(RecordingState state) => state switch
    {
        RecordingState.Paused => "paused",
        RecordingState.LoggingIn => "login",
        RecordingState.Testing => "testing",
        RecordingState.Trying => "trying",
        _ => "recording",
    };

    private List<(string Page, string Url)> OpenPages() =>
        [.. _aliases.Where(pair => !pair.Key.IsClosed).Select(pair => (pair.Value, pair.Key.Url))];

    // The login the recording started from, as Playwright saved it; null when it started from none, or it is unreadable.
    private async Task<SavedLogin?> SavedLoginAsync()
    {
        if (_options.LoadStorageStatePath is not { } path) return null;
        try
        {
            return JsonSerializer.Deserialize<SavedLogin>(await File.ReadAllTextAsync(path), Recording.Json);
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // Returns whether the page was opened during the recording, rather than by it; one given its alias, e.g. the page
    // in place of another, is not.
    private bool Track(IPage page, string? alias = null)
    {
        bool opened;
        lock (_gate)
        {
            if (_aliases.ContainsKey(page))
            {
                if (alias is not null) _aliases[page] = alias;
                return false;
            }

            opened = alias is null && _aliases.Count > 0;
            _aliases[page] = alias ?? (opened ? $"page{_aliases.Count}" : "page");
            if (_state != RecordingState.LoggingIn) WatchDialogs(page);
        }

        page.FrameNavigated += (_, frame) =>
        {
            if (frame != page.MainFrame) return;
            Log.Navigated(AliasOf(page), frame.Url, Now());
            _login?.Navigated(frame.Url);
            Tried?.Navigated(AliasOf(page), frame.Url, Now());
        };
        page.Request += (_, request) =>
        {
            if (!request.IsNavigationRequest || request.RedirectedFrom is not null || request.Frame != page.MainFrame) return;
            var byThePerson = StartedByThePersonAsync(request);
            Log.NavigationRequested(AliasOf(page), byThePerson);
            Tried?.NavigationRequested(AliasOf(page), byThePerson);
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
        page.Dialog += (_, dialog) =>
        {
            Log.BrowserDialog(AliasOf(page), dialog.Message, Now());
            Tried?.BrowserDialog(AliasOf(page), dialog.Message, Now());
        };
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

    /// <summary>The viewport of <see cref="BrowserDefaults.RecorderViewport"/>'s <c>W,H</c>.</summary>
    public static ViewportSize Viewport(string widthComma) =>
        widthComma.Split(',') is [var width, var height]
            ? new ViewportSize
            {
                Width = int.Parse(width.Trim(), CultureInfo.InvariantCulture),
                Height = int.Parse(height.Trim(), CultureInfo.InvariantCulture),
            }
            : throw new InvalidOperationException($"RecorderViewport '{widthComma}' is not W,H.");

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // Playwright's storage state: the cookies and each origin's local storage.
    private sealed class SavedLogin
    {
        public List<SavedCookie> Cookies { get; set; } = [];
        public List<SavedOrigin> Origins { get; set; } = [];
    }

    private sealed class SavedOrigin
    {
        public string Origin { get; set; } = "";
        public List<SavedItem> LocalStorage { get; set; } = [];
    }

    private sealed class SavedCookie
    {
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
        public string Domain { get; set; } = "";
        public string Path { get; set; } = "/";
        public float Expires { get; set; } = -1;
        public bool HttpOnly { get; set; }
        public bool Secure { get; set; }
        public string? SameSite { get; set; }
    }

    private sealed class SavedItem
    {
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
    }

    private sealed class ControlMessage
    {
        public string Type { get; set; } = "";

        /// <summary>For F4: the page's elements, as the panel shows them.</summary>
        public JsonElement? Tree { get; set; }

        /// <summary>For F4: what the capture script found to locate each element a click may act on.</summary>
        public List<DescribedElement>? Described { get; set; }

        /// <summary>For check mode: whether the page is in it.</summary>
        public bool? On { get; set; }

        /// <summary>For what a login flow did on the page: <c>click</c> or <c>type</c>, on an element of that role and name.</summary>
        public string? Action { get; set; }

        public string? Role { get; set; }

        public string? Name { get; set; }
    }
}
