using System.Globalization;
using System.Text.Json;
using Microsoft.Playwright;
using QaaS.Playwright.Browser;
using QaaS.Playwright.Files;
using QaaS.Playwright.Recorder.State;

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
    /// The login flow that runs first, by its class name: nothing is recorded until it logged in,
    /// and the recording starts on the page the login left. Null to record at once.
    /// </summary>
    public string? LoginFlow { get; init; }

    /// <summary>
    /// The BaseUrl the login flow runs with, as the tests give it; null to take the address of <see cref="Url"/>. A
    /// recording that starts on its root starts where the login left the app.
    /// </summary>
    public string? LoginBaseUrl { get; init; }

    /// <summary>
    /// A recording this one goes on from: its actions come first, and are replayed after the login flow, as in a test
    /// (<see cref="RecordingState.Testing"/>), before anything is recorded. Null to record anew.
    /// </summary>
    public Recording? Continues { get; init; }

    /// <summary>
    /// Whether the login flow runs on a blank page before BaseUrl opens, as the tests' probe runs its setup flows with
    /// <c>SetupFlowsBeforeBaseUrl</c>; else BaseUrl opens first, as the probe's default.
    /// </summary>
    public bool SetupFlowsBeforeBaseUrl { get; init; }

    /// <summary>The recorder's lifetime, which ends the run scopes still going however the recording ends; null in tests.</summary>
    public RecorderLifetime? Lifetime { get; init; }
}

/// <summary>
/// The person acted in the app while the recording was paused, which recorded nothing: told once a pause, the first
/// time. <paramref name="What"/> is what they did: <c>click</c>, <c>typing</c> or <c>choice</c>.
/// </summary>
internal sealed record NotRecordedWhilePaused(string What);

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
internal sealed class RecordingSession : IAsyncDisposable, ICaptureDrain, IEffects
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
    private readonly RecorderStateMachine _machine;
    private readonly Dictionary<IPage, int> _documents = [];
    private readonly List<IPage> _loginPages = [];
    private volatile LoginWatch? _login;
    private bool _notRecordedTold;
    private Task _published = Task.CompletedTask;
    private int _disposed;

    /// <summary>How long a drain or a page's acknowledgement of a new state may take; a page that does not answer is late.</summary>
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(1);

    private RecordingSession(IPlaywright playwright, IBrowser browser, IBrowserContext context, RecordingOptions options,
        Action<object> onNews)
    {
        _playwright = playwright;
        _browser = browser;
        _context = context;
        _options = options;
        _onNews = onNews;
        // As the table starts it: the login flow first, else the replay of the flow continued, else recording.
        var start = RecorderTable.Apply(RecorderState.Initial, new Started(options.LoginFlow is not null, options.Continues is not null)).State;
        Pipeline = new CapturePipeline(recording: start.LogOpen);
        Pipeline.Commit(start.Epoch, start.LogOpen);
        Log = new CaptureLog(paused: !start.LogOpen, options.Continues, Pipeline);
        if (start.Run is { } run) CurrentRun = new RunScope(run.Kind.ToString(), options.Lifetime);
        _machine = new RecorderStateMachine(this, this, start);
        _machine.Published += Publish;
    }

    /// <summary>The recorder's state: the one snapshot the console, the panel and every page render.</summary>
    public RecorderState Snapshot => _machine.State;

    /// <summary>The scope of the login, the test or the replay that runs now; null when none does.</summary>
    public RunScope? CurrentRun { get; private set; }

    /// <summary>What the panel does of a part recorded again as the state machine moves it on: begin it, keep it, drop it, go back.</summary>
    public Func<Effect, RecorderState, RecorderState, Task>? PartEffects { get; set; }

    /// <summary>The capture pipeline: the epochs of the states, which every page's action carries, and what each page told.</summary>
    public CapturePipeline Pipeline { get; }

    /// <summary>Completes once every page has the latest state, or did not answer in time.</summary>
    public Task Published
    {
        get { lock (_gate) return _published; }
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

    /// <summary>The main mode, as the console and the panel name it.</summary>
    public RecordingState State => Named(Snapshot.Mode);

    /// <summary>What the recording does once the test running ends; when none runs, what it does now.</summary>
    public RecordingState AfterTest => Snapshot is { Mode: Mode.Testing } testing ? Named(testing.Then) : State;

    /// <summary>Why the recording is paused when the person did not pause it, e.g. the login flow failed; else null.</summary>
    public string? Notice => Snapshot.Notice;

    /// <summary>
    /// Where the recording starts after the login flow when it is not the URL it was given: the app's page the login
    /// left, for a recording started on the app's root; else null.
    /// </summary>
    public string? StartedAfterLogin { get; private set; }

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
    public bool Checking => Snapshot.Check is not null;

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
    /// (<see cref="State.PartState"/>); else null.
    /// </summary>
    public string? RerecordingPart => PartLabel(Snapshot);

    /// <summary>Raised, on Playwright's thread, when the person pressed F2 in the app while recording a part again.</summary>
    public event Action? PartDone;

    /// <summary>Raised, on Playwright's thread, when the person pressed Ctrl+K in the app, for the panel's commands.</summary>
    public event Action? PaletteAsked;

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
                // Ctrl+C in the console reaches the driver too, which would close Chrome: the recording ends on it as on
                // Enter, with Chrome still open, so the login is saved as ever.
                HandleSIGINT = false,
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
    /// its token is cancelled once it was stopped. As the probe runs a setup flow, it runs first, on a page that has
    /// loaded nothing yet: a flow that plants what the app needs before its first page loads, e.g. a token with
    /// <c>AddInitScriptAsync</c>, has it on every page after. Once it logged in, the page goes to the recording's URL,
    /// unless it is there already, or that URL is the app's root and the login left the app on a page of its own. While it runs, <see cref="LoginProgress"/> says each second how it
    /// goes, and the pages' overlay shows it. <see cref="LogInByHand"/> stops it, as it stops itself after two minutes
    /// of nothing, or when the app asked the browser to sign in: its page is then replaced by a fresh one at the same
    /// address, the login so far kept, so whatever the flow waited for fails at once, and nothing it does reaches the
    /// app. The recording then starts on the page the login left, or stays paused with why, for the person to log in
    /// by hand. A recording that continues another goes on to replay it (<see cref="RunEnded"/> ends that).
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
        var scope = CurrentRun ?? new RunScope(nameof(RunKind.Login), _options.Lifetime);
        // In the probe's order: BaseUrl opened first, unless the tests' probe runs its setup flows before it.
        if (!_options.SetupFlowsBeforeBaseUrl) await OpenBaseUrlAsync();
        LoginEnd? end;
        try
        {
            var running = logIn(Page, scope.Token);
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
                // Its scope ends as the state leaves logging in: its token, and the pages it opened, which it acts on no more.
                end = new LoginEnd(await watch.ByHand, ByHand: true);
                _ = running.ContinueWith(task => _ = task.Exception, TaskScheduler.Default);
            }
        }
        finally
        {
            _login = null;
            WatchNetwork(on: false);
            LoginProgress = null;
        }

        if (end is null) StartedAfterLogin = await GoToStartAsync() is { Length: > 0 } note ? note : null;
        else if (!end.ByHand && Page.Url is "" or "about:blank") await OpenStartAsync();
        await AttachConsoleApiAsync();
        await RequestAsync(end is null ? new LoginEnded(null, Continues: _options.Continues is not null)
            : end.ByHand ? new LogInByHand(end.Why) : new LoginEnded($"{flow} failed: {end.Why}"));

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
    public async Task<Recording> StopAsync(StopReason reason = StopReason.Saved)
    {
        lock (_gate)
            if (_result is not null)
                return _result;

        try
        {
            // Nothing more is recorded; the pages drained first, as every transition does.
            await RequestAsync(new Stop(reason));
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

    /// <summary>
    /// Drains every page: what it holds back (a field being typed) is told now, and every action it began before is
    /// waited for. True when every page acknowledged in time; a page that does not answer (a browser dialog is open in
    /// it) is late, and what it tells later is still judged by the epoch it began in.
    /// </summary>
    public async Task<bool> DrainAsync(long epoch, CancellationToken cancel)
    {
        List<(IPage Page, string Alias)> pages;
        lock (_gate) pages = [.. _aliases.Where(pair => !pair.Key.IsClosed).Select(pair => (pair.Key, pair.Value))];
        var drained = await Pipeline.DrainAsync(async token =>
        {
            var answers = await Task.WhenAll(pages.Select(async each =>
                (each.Alias, Answer: await PageElements.CallAsync(each.Page, new { op = "flush" }).WaitAsync(token))));
            var marks = new Dictionary<string, long?>();
            foreach (var (alias, answer) in answers)
            {
                var flushed = answer is null ? null : JsonSerializer.Deserialize<Flushed>(answer, Recording.Json);
                marks[flushed?.Doc ?? alias] = flushed?.Seq;
            }

            return marks;
        }, AnswerTimeout, cancel);
        return drained.Complete;
    }

    /// <summary>
    /// Asks the state machine for <paramref name="command"/>: refused, nothing changes; taken, the pages were drained, its
    /// effects carried out, and the new state committed as the capture pipeline's epoch and told to every page. It returns
    /// once the pages have it, or did not answer in time.
    /// </summary>
    public async Task<Outcome> RequestAsync(Command command)
    {
        var outcome = await _machine.RequestAsync(command);
        if (outcome.Accepted) await Published;
        return outcome;
    }

    /// <summary>F8: pauses or resumes; returns the state it is in then.</summary>
    public async Task<RecordingState> TogglePauseAsync() => Named((await RequestAsync(new TogglePause())).State.Mode);

    /// <summary>F7: tries things, or stops trying; returns the state it is in then.</summary>
    public async Task<RecordingState> ToggleTryAsync() => Named((await RequestAsync(new ToggleTry())).State.Mode);

    /// <summary>The recording so far, as <see cref="StopAsync"/> would return it.</summary>
    public Recording Build() => Log.Build(_options.Url) with { After = _options.LoginFlow };

    // What a transition does beside its new state, as the table says, once the pages were drained.
    async Task IEffects.ApplyAsync(Effect effect, RecorderState before, RecorderState after)
    {
        switch (effect)
        {
            case Effect.PauseLog:
                Log.Pause(Now());
                break;
            case Effect.ResumeLog or Effect.ResumeLogRebased:
                lock (_gate) Log.Resume(OpenPages(), Now(), rebase: effect == Effect.ResumeLogRebased);
                break;
            case Effect.StartTrying:
                var tried = new CaptureLog(paused: true);
                // From the pages as they are: where they go from here is what the person tried.
                lock (_gate) tried.Resume(OpenPages(), Now());
                Tried = tried;
                break;
            case Effect.StopTrying:
                Tried = null;
                break;
            case Effect.StartRun:
                CurrentRun = new RunScope(after.Run!.Kind.ToString(), _options.Lifetime);
                break;
            case Effect.DisposeRun when CurrentRun is { } run:
                CurrentRun = null;
                await run.DisposeAsync();
                break;
            case Effect.ReplaceLoginPage:
                await ReplacePageAsync();
                break;
            case Effect.OpenPart or Effect.KeepPart or Effect.DropPart or Effect.GoBack when PartEffects is { } part:
                await part(effect, before, after);
                break;
        }
    }

    // The state committed as the next epoch, which the log takes what began in while it is open, then told to every page.
    private void Publish(RecorderState state)
    {
        Pipeline.Commit(state.Epoch, state.LogOpen);
        lock (_gate)
        {
            if (state.Mode == Mode.Paused) _notRecordedTold = false;
            if (state.Mode != Mode.LoggingIn)
            {
                WatchDialogs();
                _loginPages.Clear();
            }

            _published = ShowStateAsync(state);
        }

        CheckingChanged?.Invoke();
        // The end is the summary's to say.
        if (state.Mode is not (Mode.Saving or Mode.Done or Mode.Ended)) _onNews(Named(state.Mode));
    }

    private static RecordingState Named(Mode mode) => mode switch
    {
        Mode.Paused or Mode.Saving or Mode.Done or Mode.Ended => RecordingState.Paused,
        Mode.LoggingIn or Mode.Starting or Mode.Setup => RecordingState.LoggingIn,
        Mode.Testing or Mode.Asking => RecordingState.Testing,
        Mode.Trying => RecordingState.Trying,
        _ => RecordingState.Recording,
    };

    // What the overlay says of a part recorded again, while it replays and while it is recorded.
    private static string? PartLabel(RecorderState state) => state.Part is { Phase: PartPhase.Replaying or PartPhase.Recording } part
        ? $"{(part.After ? "Recording after" : "Re-recording")} {part.Label}"
        : null;

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
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
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

    // Guarded as every handler of Playwright's events is: one that throws ends Playwright's connection, the browser's
    // and the recording's with it.
    private void OnRequest(object? sender, IRequest request) =>
        Guarded("a request of the login", () => _login?.Requested(request, request.Url, OpensADocument(request)));

    private void OnAnswered(object? sender, IRequest request) => Guarded("an answer to the login", () => _login?.Answered(request));

    private void OnResponse(object? sender, IResponse response) => Guarded("an answer to the login", () =>
    {
        if (response.Status == 401 && OpensADocument(response.Request) && response.Headers.TryGetValue("www-authenticate", out var scheme))
            _login?.AuthAsked(scheme, response.Url);
    });

    // Whether the request is for the document of a page, not of a frame in it.
    private static bool OpensADocument(IRequest request) => request.IsNavigationRequest && FrameOf(request) is not { ParentFrame: not null };

    // The frame a request is for. The first document of a page that opens, a pop-up, is asked for before the page has
    // its frame, and Playwright then throws rather than say: it is that page's own, its main frame, so null.
    private static IFrame? FrameOf(IRequest request)
    {
        try
        {
            return request.Frame;
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    // After the login: the recording's URL, unless the page is there, or the recording starts on the app's root and the
    // login left the app on a page of its own, which is said.
    private async Task<string?> GoToStartAsync()
    {
        var stay = StartPage.StaysAfterLogin(Page.Url, _options.Url, _options.LoginBaseUrl);
        if (stay is null) await OpenStartAsync();
        return stay;
    }

    // The login's BaseUrl, as the probe opens it before its setup flows: a page that does not load is the login's to find.
    private async Task OpenBaseUrlAsync()
    {
        var baseUrl = _options.LoginBaseUrl ?? new Uri(_options.Url).GetLeftPart(UriPartial.Authority);
        try
        {
            await Page.GotoAsync(baseUrl, new() { Timeout = NavigationTimeoutMs });
        }
        catch (PlaywrightException)
        {
            // The login flow goes on from where the page is.
        }
    }

    // The recording's URL, for a person who logs in by hand there after the login flow failed before it opened a page.
    private async Task OpenStartAsync()
    {
        try
        {
            await Page.GotoAsync(_options.Url, new() { Timeout = NavigationTimeoutMs });
        }
        catch (PlaywrightException)
        {
            // The person goes on from where it is.
        }
    }

    // A fresh page in the old one's place, at the address it was at, and the old one closed: the login flow stopped
    // there fails at once, whatever it waited for, and does nothing more. The login so far is the context's, and stays.
    private async Task ReplacePageAsync()
    {
        var old = Page;
        // The pages the login flow opened go too: it acts in them no more.
        List<IPage> opened;
        lock (_gate) opened = [.. _loginPages.Where(page => page != old)];
        foreach (var popup in opened)
        {
            try
            {
                await popup.CloseAsync(new() { RunBeforeUnload = false });
            }
            catch (PlaywrightException)
            {
                // Closed already.
            }
        }

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
        await _context.ExposeBindingAsync(Binding, (BindingSource source, string json) => Guarded("an action", () =>
        {
            var page = AliasOf(source.Page);
            var news = Log.Add(page, json);
            if (news is not null) _onNews(news);
            // What the person tries is told apart: the recording took only what it began before.
            else Tried?.Add(page, json);
        }));
        await _context.ExposeBindingAsync(ExplainBinding, (BindingSource source, string json) =>
        {
            if (source.Page is { } page) _ = PageElements.CallAsync(page, OverlayLabel.Reply(json));
        });
        await _context.ExposeBindingAsync(ControlBinding, (BindingSource source, string json) => Guarded("a key or a mode", () => Control(source.Page, json)));
        await _context.AddInitScriptAsync(CaptureScript.For(BrowserDefaults.TestIdAttribute));
        _context.Page += (_, page) => Guarded("a page opened", () =>
        {
            // The first page is the one the recording opens itself; any other is a pop-up or a tab.
            if (!Track(page)) return;
            var byThePerson = OpenedByThePersonAsync(page);
            Log.PageOpened(AliasOf(page), Now(), byThePerson);
            Tried?.PageOpened(AliasOf(page), Now(), byThePerson);
        });
        _browser.Disconnected += (_, _) => _closed.TrySetResult();

        Page = await _context.NewPageAsync();
        Track(Page);
        // A login flow runs first, on a page that loaded nothing, as the probe runs a setup flow; then it goes there.
        if (_options.LoginFlow is null) await Page.GotoAsync(_options.Url, new() { Timeout = NavigationTimeoutMs });
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
                var now = Snapshot;
                _ = PageElements.CallAsync(page, new { op = "state", state = StateName(Named(now.Mode)), part = PartLabel(now), epoch = now.Epoch });
                break;
            case "pause":
                _ = TogglePauseAsync();
                break;
            case "ignored" when State == RecordingState.Paused && !_notRecordedTold:
                _notRecordedTold = true;
                // A page's own script may call it too: only what the capture script says.
                _onNews(new NotRecordedWhilePaused(message.What is "click" or "typing" or "choice" ? message.What : "action"));
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
                if (message.On == false) _ = RequestAsync(new InspectOff());
                else if (Snapshot.Inspect is null) _ = RequestAsync(new InspectOn(DocumentOf(page)));
                Inspected?.Invoke(AliasOf(page), json);
                break;
            case "checking":
                _ = RequestAsync(message.On == true ? new CheckOn(DocumentOf(page)) : new CheckOff());
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
                _ = ToggleTryAsync();
                break;
            case "done" when Snapshot.Part is { Phase: PartPhase.Recording }:
                PartDone?.Invoke();
                break;
            case "palette":
                PaletteAsked?.Invoke();
                break;
        }
    }

    // Each page's overlay shows the state, with its epoch: paused, or nothing while the login flow runs. A page loading
    // meanwhile asks.
    private Task ShowStateAsync(RecorderState state)
    {
        List<IPage> pages;
        lock (_gate) pages = [.. _aliases.Keys.Where(page => !page.IsClosed)];
        var shown = new { op = "state", state = StateName(Named(state.Mode)), part = PartLabel(state), epoch = state.Epoch };
        return Task.WhenAll(pages.Select(async page =>
        {
            try
            {
                await PageElements.CallAsync(page, shown).WaitAsync(AnswerTimeout);
            }
            catch (TimeoutException)
            {
                // A page a browser dialog blocks learns it once the dialog is answered.
            }
        }));
    }

    // A page's document, as check and inspect mode belong to: numbered anew each time it loads one.
    private PageDocument DocumentOf(IPage? page)
    {
        lock (_gate) return new PageDocument(AliasOf(page), $"{(page is null ? 0 : _documents.GetValueOrDefault(page))}");
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
            if (Snapshot.Mode != Mode.LoggingIn) WatchDialogs(page);
            else _loginPages.Add(page);
        }

        // A new document leaves the check or inspect mode of the one before, which it does not have.
        page.DOMContentLoaded += (_, _) => Guarded("a page that loaded", () =>
        {
            lock (_gate) _documents[page] = _documents.GetValueOrDefault(page) + 1;
            var held = Snapshot;
            var alias = AliasOf(page);
            if (held.Check?.Page == alias || held.Inspect?.Page == alias) _ = RequestAsync(new DocumentChanged(DocumentOf(page)));
        });
        page.FrameNavigated += (_, frame) => Guarded("a page that went elsewhere", () =>
        {
            if (frame != page.MainFrame) return;
            Log.Navigated(AliasOf(page), frame.Url, Now());
            _login?.Navigated(frame.Url);
            Tried?.Navigated(AliasOf(page), frame.Url, Now());
        });
        page.Request += (_, request) => Guarded("a page's request", () =>
        {
            if (!request.IsNavigationRequest || request.RedirectedFrom is not null || FrameOf(request) is { } frame && frame != page.MainFrame) return;
            var byThePerson = StartedByThePersonAsync(request);
            Log.NavigationRequested(AliasOf(page), byThePerson);
            Tried?.NavigationRequested(AliasOf(page), byThePerson);
        });
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
        page.Dialog += (_, dialog) => Guarded("a browser dialog", () =>
        {
            Log.BrowserDialog(AliasOf(page), dialog.Message, Now());
            Tried?.BrowserDialog(AliasOf(page), dialog.Message, Now());
        });
    }

    // What a page or Playwright tells is taken without ever failing Playwright's own dispatch, nor the page's call: a bug
    // of the recorder's in taking one thing is logged, and the recording goes on with the next.
    private static void Guarded(string what, Action take)
    {
        try
        {
            take();
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            CrashReport.Log($"The recording could not take {what}", failure);
        }
    }

    private string AliasOf(IPage? page)
    {
        lock (_gate) return page is not null && _aliases.TryGetValue(page, out var alias) ? alias : "page";
    }

    // A pop-up has the page that opened it, even one that cannot reach it (noopener); a tab the person opened has none.
    private static async Task<bool> OpenedByThePersonAsync(IPage page)
    {
        try
        {
            return await page.OpenerAsync() is null;
        }
        catch (PlaywrightException)
        {
            return false; // The page closed first.
        }
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
            FileStore.Machine.Write(path, await _context.StorageStateAsync(), ownerOnly: true);
        }
        catch (PlaywrightException)
        {
            // The browser closed while saving: the previous login stays.
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            LoginNotSaved = $"Your login was not saved to {Path.GetFullPath(path)} ({failure.Message}), so the next " +
                            "recording starts signed out. Check that you can write to that folder.";
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

    // A page's answer to a drain: its document, and the number of the last action it began.
    private sealed record Flushed(string? Doc, long? Seq);

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

        /// <summary>For what was done while paused: <c>click</c>, <c>typing</c> or <c>choice</c>.</summary>
        public string? What { get; set; }
    }
}
