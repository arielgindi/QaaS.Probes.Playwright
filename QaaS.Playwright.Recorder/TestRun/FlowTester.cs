using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Playwright.Browser;
using QaaS.Playwright.Configuration;
using QaaS.Playwright.Flows;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Generation;
using QaaS.Playwright.Recorder.Login;
using QaaS.Playwright.Recorder.Panel;
using QaaS.Playwright.Recorder.State;
using QaaS.Playwright.Reporting;

namespace QaaS.Playwright.Recorder.TestRun;

/// <summary>How a test run runs: as the probe does by default, or shorter, for the recorder's own tests.</summary>
internal sealed record TestOptions
{
    /// <summary>The longest a Playwright action waits, in milliseconds: the probe's DefaultTimeout.</summary>
    public int DefaultTimeout { get; init; } = new PlaywrightFlowConfig().DefaultTimeout;

    /// <summary>How long slow motion waits before each action, in milliseconds, for a person to watch it.</summary>
    public int SlowMotionMs { get; init; } = 500;
}

/// <summary>
/// Tests the flow recorded so far, while recording. It builds the flow exactly as it will be saved
/// (<see cref="FlowBuild"/>) and runs it as the probe runs it: in a browser context of its own in the recorder's Chrome,
/// in a window over the recorded one, with the page the recording started on as BaseUrl, the recording's login flow
/// first when it has one, and the recorded values as the flow's settings, through the probe's own BrowserSession and
/// FlowRunner, which set its pages up, time it out, hide its secrets and shoot its failure. The recording is paused
/// meanwhile. Each phase before the first action, the build, the window, BaseUrl, the login, is told as it begins, and
/// how long each phase took once the run ended. Each action is told as it begins, passes or fails, and a run can stop
/// after an action ("Run to here"), or be stopped. One run at a time. The context is opened by the recording's own connection to Chrome: another one would
/// reset the emulation of every page it attaches to, the recorded one's among them, e.g. its colour scheme. A replay
/// runs the flow so far in the recorded window itself instead, for a recording that continues another to go on there,
/// or runs it from the start there, up to a part the person records again.
/// </summary>
internal sealed class FlowTester(RecordingSession session, TestOptions? options = null) : IAsyncDisposable
{
    private readonly TestOptions _options = options ?? new();
    private readonly FlowBuild _build = new();
    private readonly Lock _gate = new();
    private TestReport? _report;
    private Task<TestReport>? _running;
    private CancellationTokenSource? _stop;
    private int _runs;

    /// <summary>Raised, on any thread, whenever <see cref="Report"/> changes.</summary>
    public event Action? Changed;

    /// <summary>The run going on, else the last one; null before the first, or once cleared.</summary>
    public TestReport? Report
    {
        get { lock (_gate) return _report; }
    }

    /// <summary>The last run's build, weakly: once it is gone, the run left nothing loaded.</summary>
    internal WeakReference? LastBuild { get; private set; }

    /// <summary>
    /// Tests the flow as <paramref name="request"/> names it and saves it, in <paramref name="namespaceName"/>: the
    /// whole of it, or up to the action <paramref name="upTo"/> and including it. <paramref name="front"/>, the panel,
    /// stays in front of the run's window. A run going on already is returned instead. It never throws: what went wrong
    /// is in the report.
    /// </summary>
    public Task<TestReport> RunAsync(RecordRequest request, string namespaceName, string? upTo = null, bool slowMotion = false,
        IPage? front = null) => Start(request, namespaceName, upTo, slowMotion, front, replay: false, fromStart: false);

    /// <summary>
    /// Replays the flow so far, as a test runs it but on the recorded page itself, from where it is: the recording
    /// continues another (<see cref="RecordingOptions.Continues"/>), and goes on where its actions leave the app. The
    /// session is in its Testing state from its start, and stays in it: its caller ends it. It never throws.
    /// </summary>
    public Task<TestReport> ReplayAsync(RecordRequest request, string namespaceName) =>
        Start(request, namespaceName, null, false, null, replay: true, fromStart: false);

    /// <summary>
    /// Replays the flow from the recording's start in the recorded window, as a test runs it: the page back to how the
    /// recording started (<see cref="RecordingSession.ResetToStartAsync"/>), BaseUrl, the login flow, then the flow up to
    /// the action <paramref name="upTo"/>, that one too, or none of it when null; the app is then where a part after it
    /// starts. The session is in its Testing state already (<see cref="State.Rerecord"/>), and stays in it.
    /// It never throws.
    /// </summary>
    public Task<TestReport> ReplayFromStartAsync(RecordRequest request, string namespaceName, string? upTo) =>
        Start(request, namespaceName, upTo, false, null, replay: true, fromStart: true);

    /// <summary>
    /// Has the compiler ready for the first test, in the background, by compiling the flow as recorded so far once
    /// (<see cref="FlowBuild.WarmUp"/>).
    /// </summary>
    public Task WarmUp(RecordRequest request) =>
        FlowBuild.WarmUp(() => FlowCodeGenerator.Generate(session.Build(), request.ClassName, "Flows", request.AutoChecks));

    private Task<TestReport> Start(RecordRequest request, string namespaceName, string? upTo, bool slowMotion, IPage? front,
        bool replay, bool fromStart)
    {
        lock (_gate)
        {
            if (_running is { IsCompleted: false } running) return running;
            _stop?.Dispose();
            var stop = _stop = new CancellationTokenSource();
            return _running = Task.Run(() => RunOnceAsync(request, namespaceName, upTo, slowMotion, front, replay, fromStart, stop.Token));
        }
    }

    /// <summary>How a run ended, as the state machine takes it.</summary>
    public static RunOutcome OutcomeOf(TestReport report) => report.Outcome switch
    {
        TestOutcome.Passed => RunOutcome.Passed,
        TestOutcome.Stopped => RunOutcome.Stopped,
        _ => RunOutcome.Failed,
    };

    /// <summary>Stops the run going on, and completes once it ended.</summary>
    public async Task StopAsync()
    {
        Task? running;
        CancellationTokenSource? stop;
        lock (_gate) (running, stop) = (_running, _stop);
        if (running is not { IsCompleted: false }) return;
        await stop!.CancelAsync();
        await running;
    }

    /// <summary>Forgets the last run, unless one is going on.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (_running is { IsCompleted: false }) return;
            _report = null;
        }

        Changed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _stop?.Dispose();
        _build.Dispose();
    }

    private async Task<TestReport> RunOnceAsync(RecordRequest request, string namespaceName, string? upTo, bool slowMotion,
        IPage? front, bool replay, bool fromStart, CancellationToken stop)
    {
        var recording = session.Build();
        var (plan, flow) = FlowCodeGenerator.PlanAndWrite(recording, request.ClassName, namespaceName, request.AutoChecks);
        // In the order the flow does them, as the panel lists them; a loop's repeat by repeat.
        var order = plan.Steps.SelectMany(step => step.Actions).Select(action => action.Source.Id)
            .Where(flow.ActionLines.ContainsKey).Distinct().ToList();
        var started = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var report = new TestReport(Interlocked.Increment(ref _runs), request.ClassName, flow.Code, order)
        {
            UpTo = upTo is not null && order.Contains(upTo) ? upTo : null, SlowMotion = slowMotion,
            Login = replay && !fromStart ? null : request.Login?.Flow, Started = started, Since = started, Replay = replay,
            FromStart = fromStart,
        };
        if (order.Count == 0)
            return Publish(report with { Phase = TestPhase.Done, Outcome = TestOutcome.NotRun, Problem = "Record a step first: there is nothing to test yet." });
        // A test is a state of the recorder's, whose scope Stop ends; a replay runs in the state that asked for it.
        var asked = replay ? null : await session.RequestAsync(new Test());
        if (asked is { Accepted: false })
            return Publish(report with { Phase = TestPhase.Done, Outcome = TestOutcome.NotRun, Problem = asked.Refused });
        var run = (asked?.State ?? session.Snapshot).Run;
        using var stopped = (session.CurrentRun?.Token ?? CancellationToken.None).Register(() => _stop?.Cancel());

        var times = new PhaseTimes();
        times.Begin(TestPhase.Building);
        Publish(report);
        ConsoleUi.Testing(report);
        try
        {
            report = await TestAsync(report, flow, request, front, times, stop, recording);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            report = report with { Outcome = TestOutcome.NotRun, Problem = $"The test could not run: {FlowRunner.Describe(failure)}" };
        }
        finally
        {
            // Stopped first, the run ended already, and this changes nothing.
            if (!replay && run is not null) await session.RequestAsync(new RunEnded(run.Id, OutcomeOf(report)));
        }

        report = Publish(report with { Phase = TestPhase.Done, Times = times.End() });
        ConsoleUi.Tested(report, recording);
        return report;
    }

    private async Task<TestReport> TestAsync(TestReport report, GeneratedFlow flow, RecordRequest request, IPage? front,
        PhaseTimes times, CancellationToken stop, Recording recording)
    {
        BuiltFlow? built;
        IConfiguration flowConfiguration;
        string? login;
        try
        {
            // A replay to before the flow's first action needs no build.
            built = report.Total > 0 ? await _build.BuildAsync(flow, stop) : null;
            flowConfiguration = await SettingsAsync(flow, request, stop);
            // A flow that logs in itself starts signed out, as it was recorded.
            login = recording.LogsIn ? null : await _build.CopyLoginAsync(session.Options.LoadStorageStatePath, stop);
        }
        catch (FlowBuildException failure)
        {
            return report with { Outcome = TestOutcome.NotRun, Problem = failure.Message, ProblemDetail = failure.Detail };
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return report with { Outcome = TestOutcome.Stopped };
        }
        catch (InvalidOperationException unreadable)
        {
            // The login flow's settings.
            return report with { Outcome = TestOutcome.NotRun, Problem = unreadable.Message };
        }

        var secrets = Secrets.Of(flowConfiguration, "");
        var log = new TestLog(secrets);
        var viewport = RecordingSession.Viewport(BrowserDefaults.RecorderViewport);
        var config = new PlaywrightFlowConfig
        {
            BaseUrl = session.Options.Url, Headless = false, SlowMo = 0,
            DefaultTimeout = _options.DefaultTimeout, ViewportWidth = viewport.Width, ViewportHeight = viewport.Height,
            LoadStorageStatePath = login,
        };

        var browser = session.Page.Context.Browser ?? throw new PlaywrightException("The recording has no browser.");
        if (stop.IsCancellationRequested) return report with { Outcome = TestOutcome.Stopped };

        var timer = Stopwatch.StartNew();
        // A replay goes on in the recorded window, after the login the recording ran, or from how the recording started,
        // and is stopped between statements.
        var replay = report.Replay;
        if (!replay) report = Enter(report, TestPhase.Opening, times);
        if (report.FromStart)
        {
            report = Enter(report, TestPhase.StartPage, times);
            await session.ResetToStartAsync();
        }

        await using var run = replay
            ? await BrowserSession.OnPageAsync(session.Page, config, log)
            : await BrowserSession.OpenAsync(browser, config, log);
        await using var closing = stop.Register(() => { if (!replay) _ = CloseAsync(run.Page.Context); });
        if (!replay) await PanelWindow.PlaceOverAsync(session.Page, run.Page);
        if (front is not null) await BringToFrontAsync(front);

        var steps = new Steps(report.Actions, flow.Statements, report.Total - 1);
        report = await RunFlowsAsync(report, flow, built, replay && !report.FromStart ? null : request.Login, flowConfiguration,
            secrets, log, config, run, steps, times, stop);
        return report with { Steps = steps.Snapshot(), Ms = timer.ElapsedMilliseconds, Warnings = log.Warnings };
    }

    // BaseUrl, the login flow and the flow, as the probe runs them; whatever ends the run, the state of each action.
    private async Task<TestReport> RunFlowsAsync(TestReport report, GeneratedFlow flow, BuiltFlow? built, LoginFlow? login,
        IConfiguration flowConfiguration, Secrets secrets, ILogger log, PlaywrightFlowConfig config, BrowserSession run,
        Steps steps, PhaseTimes times, CancellationToken stop)
    {
        var context = new Context { Logger = NullLogger.Instance };
        string? inLogin = null;
        try
        {
            // The login flow runs first, on a page that loaded nothing, with the BaseUrl the tests give it, as the
            // recording ran it; then the page goes to the recording's URL, as the recording did.
            if (login is not null)
            {
                if (run.Page.Url is not ("" or "about:blank")) await run.Page.GotoAsync("about:blank");
                report = Enter(report, TestPhase.LoggingIn, times);
                inLogin = login.Flow;
                FlowAssemblyContext.Load(login.Assembly);
                var startUrl = config.BaseUrl;
                var (loginUrl, _) = login.BaseUrlFor(startUrl);
                config.BaseUrl = loginUrl;
                try
                {
                    await new FlowRunner(context, null, null, config, flowConfiguration, secrets, log).RunAsync([login.Flow], run);
                }
                finally
                {
                    config.BaseUrl = startUrl;
                }

                inLogin = null;
                report = Enter(report, TestPhase.StartPage, times);
                if (StartPage.StaysAfterLogin(run.Page.Url, startUrl, loginUrl) is null) await run.OpenBaseUrlAsync(startUrl);
            }
            else if (!report.Replay || report.FromStart)
            {
                if (report.Phase != TestPhase.StartPage) report = Enter(report, TestPhase.StartPage, times);
                await run.OpenBaseUrlAsync(config.BaseUrl);
            }

            report = Enter(report, TestPhase.Running, times);
            if (built is not null) await RunBuiltAsync(report, built, context, flowConfiguration, secrets, log, config, run, steps, stop);
            steps.Ended(failed: null);
            return report with { Outcome = TestOutcome.Passed };
        }
        catch (Exception) when (stop.IsCancellationRequested || run.Page.IsClosed)
        {
            steps.Ended(failed: null, stopped: true);
            return report with { Outcome = TestOutcome.Stopped };
        }
        catch (FlowStoppedException)
        {
            // It ran to the action it was to run to.
            steps.Ended(failed: null);
            return report with { Outcome = TestOutcome.Passed };
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            var described = Describe(failure, context, flow, built, steps, secrets, inLogin, login, config.BaseUrl);
            steps.Ended(failed: described.Action);
            return report with { Outcome = TestOutcome.Failed, Failure = described };
        }
    }

    // The build, loaded for this run alone and unloaded after it, run by the probe's FlowRunner as any flow.
    private async Task RunBuiltAsync(TestReport report, BuiltFlow built, Context context, IConfiguration flowConfiguration,
        Secrets secrets, ILogger log, PlaywrightFlowConfig config, BrowserSession run, Steps steps, CancellationToken stop)
    {
        var loaded = TestFlowContext.Load(built);
        try
        {
            loaded.OnStatement(async statement =>
            {
                // Before the action after the one to run to, or once stopped: the flow ends there.
                var reached = stop.IsCancellationRequested ? Reached.Beyond : steps.At(statement);
                if (reached == Reached.Beyond) throw new FlowStoppedException();
                Publish(report with { Steps = steps.Snapshot(), Running = steps.Statement });
                if (reached == Reached.Next && report.SlowMotion) await Task.Delay(_options.SlowMotionMs, stop);
            });
            var className = report.Flow;
            await new FlowRunner(context, null, null, config, flowConfiguration, secrets, log, _ => loaded.Create(className))
                .RunAsync([className], run);
        }
        finally
        {
            loaded.Release();
            LastBuild = new WeakReference(loaded);
        }
    }

    // What failed, where, and why: the failure as the probe recorded it; the action running then, as the statements
    // told it, a loop's repeat among them; the line of its stack frame in the code as saved, else of the statement
    // running; and what the writer listed to check by hand in the action's lines.
    private static TestFailure Describe(Exception failure, Context context, GeneratedFlow flow, BuiltFlow? built, Steps steps,
        Secrets secrets, string? inLogin, LoginFlow? login, string startUrl)
    {
        var outcome = PlaywrightFlowResults.Read(context, null).LastOrDefault(recorded => !recorded.Passed);
        var message = outcome?.FailureMessage ?? secrets.Scrub(FlowRunner.Describe(failure));
        var action = inLogin is null ? steps.Current : null;
        var line = action is null || built is null ? null : LineOf(failure, built.Source) ?? steps.Statement?.First;
        var span = action is null ? null : flow.ActionLines[action];
        var statement = flow.Statements.FirstOrDefault(statement => statement.Lines.First <= line && line <= statement.Lines.Last);
        var opening = flow.Statements.FirstOrDefault(statement => statement.Actions.Contains(action));

        List<string> hints = [];
        if (statement is not null && statement != opening
            && flow.Code.Split('\n')[statement.Lines.First - 1].Contains("Expect(", StringComparison.Ordinal))
            hints.Add("The action itself ran; then the page did not do what it did when you recorded it.");
        if (message.Contains("strict mode violation", StringComparison.Ordinal))
            hints.Add("Its locator finds more than one element now: choose another one for this step in the inspector.");
        if (inLogin is not null && login is not null)
        {
            if (FlowCheck.Explain(failure, inLogin) is { } plainly) hints.Add(plainly);
            var (baseUrl, from) = login.BaseUrlFor(startUrl);
            hints.Add(secrets.Scrub(LoginRunner.WhereItWas(inLogin, failure, outcome?.FailureUrl, baseUrl, from)));
            hints.Add($"{inLogin} is your tests' own flow, from {ConsoleUi.Relative(login.Assembly)}: check it, its values " +
                      "and its build, or record with --no-login.");
        }

        return new TestFailure(FlowReport.Summarize(message))
        {
            Headline = outcome is not null ? FlowReport.Describe(outcome) : FlowReport.Summarize(message),
            Action = action, Line = line, WaitingFor = WaitedFor(message), Url = outcome?.FailureUrl,
            Screenshot = outcome?.FailureScreenshot is { } png ? $"data:image/png;base64,{Convert.ToBase64String(png)}" : null,
            CallLog = FlowReport.CallLog(message), InLogin = inLogin,
            Checks = span is null ? [] : [.. flow.Checks.Where(check => check.Line >= span.First && check.Line <= span.Last)
                .Select(check => check.Message)],
            Hints = hints,
        };
    }

    // The element the call log says Playwright was waiting for, without the words.
    private static string? WaitedFor(string message) =>
        FlowReport.WaitingFor(message) is { } waiting && waiting.StartsWith("waiting for ", StringComparison.Ordinal)
            ? waiting["waiting for ".Length..]
            : FlowReport.WaitingFor(message);

    // The innermost frame in the build's code: the line of the flow, as saved, that threw or awaited what threw.
    private static int? LineOf(Exception failure, string source)
    {
        for (var exception = failure; exception is not null; exception = exception.InnerException)
            foreach (var frame in new StackTrace(exception, fNeedFileInfo: true).GetFrames())
                if (frame.GetFileLineNumber() > 0 && string.Equals(frame.GetFileName(), source, StringComparison.OrdinalIgnoreCase))
                    return frame.GetFileLineNumber();
        return null;
    }

    // The recorded values, as the probe reads them from the flow's YAML, and the login flow's settings.
    private async Task<IConfiguration> SettingsAsync(GeneratedFlow flow, RecordRequest request, CancellationToken stop)
    {
        var sections = new List<IConfiguration>();
        if (flow.Yaml.Split('\n').Any(line => line.StartsWith("    ", StringComparison.Ordinal)))
            sections.Add(LoginSettings.Read(await _build.WriteSettingsAsync(flow, stop), request.ClassName));
        if (request.Login is { } login) sections.Add(LoginSettings.Read(login.Settings, login.Flow));

        var values = new Dictionary<string, string?>();
        foreach (var section in sections)
        foreach (var (key, value) in section.AsEnumerable(makePathsRelative: true))
            values[$"{PlaywrightFlowConfig.FlowConfigurationKey}:{key}"] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection(PlaywrightFlowConfig.FlowConfigurationKey);
    }

    // The run goes on to its next phase, which the panel shows with its time, and the console once it ended.
    private TestReport Enter(TestReport report, TestPhase phase, PhaseTimes times)
    {
        times.Begin(phase);
        return Publish(report with { Phase = phase, Since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
    }

    private TestReport Publish(TestReport report)
    {
        lock (_gate) _report = report;
        Changed?.Invoke();
        return report;
    }

    private static async Task CloseAsync(IBrowserContext context)
    {
        try
        {
            await context.CloseAsync();
        }
        catch (PlaywrightException)
        {
            // Closed already.
        }
    }

    private static async Task BringToFrontAsync(IPage page)
    {
        try
        {
            await page.BringToFrontAsync();
        }
        catch (PlaywrightException)
        {
            // The panel closed.
        }
    }

    /// <summary>How long each phase of one run took, on the run's own flow of control.</summary>
    private sealed class PhaseTimes
    {
        private readonly List<PhaseTime> _ended = [];
        private readonly Stopwatch _clock = new();
        private TestPhase? _phase;

        /// <summary>The phase before ends, and <paramref name="phase"/> begins.</summary>
        public void Begin(TestPhase phase)
        {
            End();
            _phase = phase;
            _clock.Restart();
        }

        /// <summary>The phase going on ends; each phase's time, in the order they ran.</summary>
        public IReadOnlyList<PhaseTime> End()
        {
            if (_phase is { } phase) _ended.Add(new PhaseTime(NameOf(phase), _clock.ElapsedMilliseconds));
            _phase = null;
            return [.. _ended];
        }

        private static string NameOf(TestPhase phase) => phase switch
        {
            TestPhase.Building => "build",
            TestPhase.Opening => "window",
            TestPhase.StartPage => "start page",
            TestPhase.LoggingIn => "login",
            _ => "run",
        };
    }

    /// <summary>What a statement beginning means for the run.</summary>
    private enum Reached
    {
        /// <summary>The action running goes on.</summary>
        Same,

        /// <summary>Another action begins.</summary>
        Next,

        /// <summary>The action is past the one the run goes to: the run ends before it.</summary>
        Beyond,
    }

    /// <summary>
    /// The state of each action in one run, and the one running, as the statements tell it, safe to call from the flow's
    /// thread. A statement does its actions in order: in a loop's body, the first time the first repeat's, then the
    /// second's, and so on.
    /// </summary>
    private sealed class Steps(IReadOnlyList<string> order, IReadOnlyList<ActionStatement> statements, int last)
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, StepResult> _states = [];
        private readonly Stopwatch _clock = new();
        private readonly int[] _begun = new int[statements.Count];
        private string? _current;
        private LineSpan? _statement;

        /// <summary>The action running, or the last that ran.</summary>
        public string? Current
        {
            get { lock (_gate) return _current; }
        }

        /// <summary>The lines of the statement running.</summary>
        public LineSpan? Statement
        {
            get { lock (_gate) return _statement; }
        }

        /// <summary>The statement numbered <paramref name="statement"/> begins.</summary>
        public Reached At(int statement)
        {
            lock (_gate)
            {
                var actions = statements[statement].Actions;
                var action = actions[Math.Min(_begun[statement]++, actions.Count - 1)];
                _statement = statements[statement].Lines;
                if (action == _current) return Reached.Same;
                var index = order.TakeWhile(other => other != action).Count();
                if (index > last) return Reached.Beyond;
                PassCurrent();
                _current = action;
                _states[action] = new StepResult(StepState.Running, Since: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                _clock.Restart();
                return Reached.Next;
            }
        }

        /// <summary>
        /// The run ended: the action running passed, or <paramref name="failed"/> failed, or the run was stopped; the
        /// actions it did not get to did not run.
        /// </summary>
        public void Ended(string? failed, bool stopped = false)
        {
            lock (_gate)
            {
                if (failed is not null)
                    _states[failed] = new StepResult(StepState.Failed, Ms: _current == failed ? _clock.ElapsedMilliseconds : null);
                else if (stopped && _current is { } running && _states[running].State == StepState.Running)
                    _states.Remove(running);
                else
                    PassCurrent();
                foreach (var action in order.Where(action => !_states.ContainsKey(action)))
                    _states[action] = new StepResult(StepState.NotRun);
            }
        }

        public IReadOnlyDictionary<string, StepResult> Snapshot()
        {
            lock (_gate) return new Dictionary<string, StepResult>(_states);
        }

        private void PassCurrent()
        {
            if (_current is { } running && _states[running].State == StepState.Running)
                _states[running] = new StepResult(StepState.Passed, Ms: _clock.ElapsedMilliseconds);
        }
    }

    // The run's warnings, as the probe would report them, secrets hidden; nothing else it logs is shown.
    private sealed class TestLog(Secrets secrets) : ILogger
    {
        private readonly List<string> _warnings = [];

        public IReadOnlyList<string> Warnings
        {
            get { lock (_warnings) return [.. _warnings.Distinct()]; }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) lock (_warnings) _warnings.Add(secrets.Scrub(formatter(state, exception)));
        }
    }
}
