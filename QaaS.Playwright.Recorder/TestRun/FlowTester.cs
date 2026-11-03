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
/// meanwhile. Each action is told as it begins, passes or fails, and a run can stop after an action ("Run to here"), or
/// be stopped. One run at a time. The context is opened by the recording's own connection to Chrome: another one would
/// reset the emulation of every page it attaches to, the recorded one's among them, e.g. its colour scheme.
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
        IPage? front = null)
    {
        lock (_gate)
        {
            if (_running is { IsCompleted: false } running) return running;
            _stop?.Dispose();
            var stop = _stop = new CancellationTokenSource();
            return _running = Task.Run(() => RunOnceAsync(request, namespaceName, upTo, slowMotion, front, stop.Token));
        }
    }

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
        IPage? front, CancellationToken stop)
    {
        var recording = session.Build();
        var flow = FlowCodeGenerator.Generate(recording, request.ClassName, namespaceName);
        var order = flow.ActionLines.OrderBy(pair => pair.Value.First).Select(pair => pair.Key).ToList();
        var report = new TestReport(Interlocked.Increment(ref _runs), request.ClassName, flow.Code, order)
        {
            UpTo = upTo is not null && order.Contains(upTo) ? upTo : null, SlowMotion = slowMotion,
            Login = request.Login?.Flow, Started = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        if (order.Count == 0)
            return Publish(report with { Phase = TestPhase.Done, Outcome = TestOutcome.NotRun, Problem = "Record a step first: there is nothing to test yet." });
        if (!session.BeginTest())
            return Publish(report with { Phase = TestPhase.Done, Outcome = TestOutcome.NotRun, Problem = "Wait for the login flow: the flow can be tested once the recording starts." });

        Publish(report);
        ConsoleUi.Testing(report);
        try
        {
            report = await TestAsync(report, flow, request, front, stop);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            report = report with { Outcome = TestOutcome.NotRun, Problem = $"The test could not run: {FlowRunner.Describe(failure)}" };
        }
        finally
        {
            session.EndTest();
        }

        report = Publish(report with { Phase = TestPhase.Done });
        ConsoleUi.Tested(report, recording);
        return report;
    }

    private async Task<TestReport> TestAsync(TestReport report, GeneratedFlow flow, RecordRequest request, IPage? front,
        CancellationToken stop)
    {
        BuiltFlow built;
        IConfiguration flowConfiguration;
        string? login;
        try
        {
            built = await _build.BuildAsync(flow, report.Actions, stop);
            flowConfiguration = await SettingsAsync(flow, request, stop);
            login = await _build.CopyLoginAsync(session.Options.LoadStorageStatePath, stop);
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
        await using var run = await BrowserSession.OpenAsync(browser, config, log);
        await using var closing = stop.Register(() => _ = CloseAsync(run.Page.Context));
        await PanelWindow.PlaceOverAsync(session.Page, run.Page);
        if (front is not null) await BringToFrontAsync(front);

        var steps = new Steps(report.Actions, report.Total - 1);
        report = await RunFlowsAsync(report, flow, built, request.Login, flowConfiguration, secrets, log, config, run, steps, stop);
        return report with { Steps = steps.Snapshot(), Ms = timer.ElapsedMilliseconds, Warnings = log.Warnings };
    }

    // BaseUrl, the login flow and the flow, as the probe runs them; whatever ends the run, the state of each action.
    private async Task<TestReport> RunFlowsAsync(TestReport report, GeneratedFlow flow, BuiltFlow built, LoginFlow? login,
        IConfiguration flowConfiguration, Secrets secrets, ILogger log, PlaywrightFlowConfig config, BrowserSession run,
        Steps steps, CancellationToken stop)
    {
        var context = new Context { Logger = NullLogger.Instance };
        string? inLogin = null;
        try
        {
            await run.OpenBaseUrlAsync(config.BaseUrl);
            if (login is not null)
            {
                report = Publish(report with { Phase = TestPhase.LoggingIn });
                inLogin = login.Flow;
                FlowAssemblyContext.Load(login.Assembly);
                await new FlowRunner(context, null, null, config, flowConfiguration, secrets, log).RunAsync([login.Flow], run);
                inLogin = null;
            }

            report = Publish(report with { Phase = TestPhase.Running });
            await RunBuiltAsync(report, built, context, flowConfiguration, secrets, log, config, run, steps, stop);
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
            var described = Describe(failure, context, flow, built, steps, secrets, inLogin, login);
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
            loaded.OnAction(async number =>
            {
                // Before the action after the one to run to, or once stopped: the flow ends there.
                if (stop.IsCancellationRequested || !steps.Begin(number)) throw new FlowStoppedException();
                Publish(report with { Steps = steps.Snapshot() });
                if (report.SlowMotion) await Task.Delay(_options.SlowMotionMs, stop);
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

    // What failed, where, and why: the failure as the probe recorded it, its line in the code as saved, mapped to its
    // action by the writer's line map, and what that action listed to check by hand.
    private static TestFailure Describe(Exception failure, Context context, GeneratedFlow flow, BuiltFlow built, Steps steps,
        Secrets secrets, string? inLogin, LoginFlow? login)
    {
        var outcome = PlaywrightFlowResults.Read(context, null).LastOrDefault(recorded => !recorded.Passed);
        var message = outcome?.FailureMessage ?? secrets.Scrub(FlowRunner.Describe(failure));
        var line = inLogin is null ? LineOf(failure, built.Source) : null;
        var action = line is { } at ? flow.ActionLines.FirstOrDefault(pair => pair.Value.First <= at && at <= pair.Value.Last).Key : null;
        action ??= inLogin is null ? steps.Current : null;
        var span = action is null ? null : flow.ActionLines[action];
        var lineText = line is { } number ? flow.Code.Split('\n')[number - 1] : "";

        List<string> hints = [];
        if (span is not null && line > span.First && lineText.Contains("Expect(", StringComparison.Ordinal))
            hints.Add("The action itself ran; then the page did not do what it did when you recorded it.");
        if (message.Contains("strict mode violation", StringComparison.Ordinal))
            hints.Add("Its locator finds more than one element now: choose another one for this step in the inspector.");
        if (inLogin is not null && login is not null)
            hints.Add($"{inLogin} is your tests' own flow, from {Path.GetFileName(login.Assembly)}: check it, its settings " +
                      "and its build, or record with --no-login.");

        return new TestFailure(FlowReport.Summarize(message))
        {
            Headline = outcome is not null ? FlowReport.Describe(outcome) : FlowReport.Summarize(message),
            Action = action, Line = line ?? span?.First, WaitingFor = WaitedFor(message), Url = outcome?.FailureUrl,
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

    /// <summary>The state of each action in one run, and the one running, safe to call from the flow's thread.</summary>
    private sealed class Steps(IReadOnlyList<string> order, int last)
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, StepResult> _states = [];
        private readonly Stopwatch _clock = new();
        private int _current = -1;

        /// <summary>The action running, or the last that ran.</summary>
        public string? Current
        {
            get { lock (_gate) return Running; }
        }

        private string? Running => _current >= 0 ? order[_current] : null;

        /// <summary>The action numbered <paramref name="number"/> begins, and the one before passed; false past the last.</summary>
        public bool Begin(int number)
        {
            lock (_gate)
            {
                if (number > last) return false;
                PassCurrent();
                _current = number;
                _states[order[number]] = new StepResult(StepState.Running, Since: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                _clock.Restart();
                return true;
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
                    _states[failed] = new StepResult(StepState.Failed, Ms: Running == failed ? _clock.ElapsedMilliseconds : null);
                else if (stopped && Running is { } running && _states[running].State == StepState.Running)
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
            if (_current >= 0 && _states[order[_current]].State == StepState.Running)
                _states[order[_current]] = new StepResult(StepState.Passed, Ms: _clock.ElapsedMilliseconds);
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
