namespace QaaS.Playwright.Recorder.TestRun;

/// <summary>Where a test run is: building the flow, logging in, running it, or done.</summary>
internal enum TestPhase
{
    Building,
    LoggingIn,
    Running,
    Done,
}

/// <summary>How a test run ended.</summary>
internal enum TestOutcome
{
    /// <summary>Every step it ran passed: the whole flow, or the steps up to the one it ran to.</summary>
    Passed,

    /// <summary>A step failed, or what runs before the first: opening BaseUrl, the login flow, the settings.</summary>
    Failed,

    /// <summary>The person stopped it, or closed its window.</summary>
    Stopped,

    /// <summary>It could not run: the flow did not build, the .NET SDK is missing, or no window opened.</summary>
    NotRun,
}

/// <summary>How far an action of the flow got in a test run.</summary>
internal enum StepState
{
    Running,
    Passed,
    Failed,

    /// <summary>The run ended before it: a step before it failed, or it comes after the one the run went to.</summary>
    NotRun,
}

/// <summary>An action's state in a test run, how long it took, and since when it runs, in Unix milliseconds.</summary>
internal sealed record StepResult(StepState State, long? Ms = null, long? Since = null);

/// <summary>Why a test run failed, where, and what the page looked like then.</summary>
/// <param name="Message">The failure message on one line, without Playwright's call log, its secrets hidden.</param>
internal sealed record TestFailure(string Message)
{
    /// <summary>The failure on one line, as the probe's report tells it, with what it waited for and on which page.</summary>
    public string Headline { get; init; } = "";

    /// <summary>The action whose lines failed, by its id; null when the run failed before its first.</summary>
    public string? Action { get; init; }

    /// <summary>The line of the flow's code that failed, numbered as it will be saved.</summary>
    public int? Line { get; init; }

    /// <summary>The element Playwright was waiting for, as its call log names it.</summary>
    public string? WaitingFor { get; init; }

    /// <summary>The address of the page the flow was on.</summary>
    public string? Url { get; init; }

    /// <summary>The probe's failure screenshot of that page, as a <c>data:</c> URL.</summary>
    public string? Screenshot { get; init; }

    /// <summary>Playwright's call log: what it did and waited for.</summary>
    public string? CallLog { get; init; }

    /// <summary>The login flow, when it is what failed.</summary>
    public string? InLogin { get; init; }

    /// <summary>What the recorder listed to check by hand in the failed action's lines.</summary>
    public IReadOnlyList<string> Checks { get; init; } = [];

    /// <summary>What the failure likely means, when the recorder can tell.</summary>
    public IReadOnlyList<string> Hints { get; init; } = [];
}

/// <summary>
/// A test run of the flow as recorded so far, while it runs and once it ended: the code it ran, each action's state in
/// the order they run, and how it ended.
/// </summary>
/// <param name="Run">The run's number in the recording, from 1.</param>
/// <param name="Flow">The flow's class name.</param>
/// <param name="Code">The flow's code, as it will be saved.</param>
/// <param name="Actions">The flow's actions in the order they run, by their ids.</param>
internal sealed record TestReport(int Run, string Flow, string Code, IReadOnlyList<string> Actions)
{
    public TestPhase Phase { get; init; }

    /// <summary>Null until it ended.</summary>
    public TestOutcome? Outcome { get; init; }

    /// <summary>The action "Run to here" runs to, including it; null for the whole flow.</summary>
    public string? UpTo { get; init; }

    public bool SlowMotion { get; init; }

    /// <summary>A replay of the recording a recording continues, in the recorded window (<see cref="FlowTester.ReplayAsync"/>).</summary>
    public bool Replay { get; init; }

    /// <summary>
    /// A replay from the recording's start, as a test runs, in the recorded window: up to <see cref="UpTo"/>, or none of
    /// the flow's actions when it is null, for a part to be recorded again from there.
    /// </summary>
    public bool FromStart { get; init; }

    /// <summary>The login flow that runs first, by its class name.</summary>
    public string? Login { get; init; }

    /// <summary>When it started, in Unix milliseconds.</summary>
    public long Started { get; init; }

    /// <summary>How long the browser's part took, from opening BaseUrl to the end; null before it ended.</summary>
    public long? Ms { get; init; }

    /// <summary>The lines of the statement running; in a loop, of its body's statement, which every repeat runs.</summary>
    public Generation.LineSpan? Running { get; init; }

    /// <summary>Each action's state, by its id; an action the run has not reached yet has none.</summary>
    public IReadOnlyDictionary<string, StepResult> Steps { get; init; } = new Dictionary<string, StepResult>();

    public TestFailure? Failure { get; init; }

    /// <summary>Why it could not run, for <see cref="TestOutcome.NotRun"/>.</summary>
    public string? Problem { get; init; }

    /// <summary>More about the problem, e.g. the compiler's errors.</summary>
    public string? ProblemDetail { get; init; }

    /// <summary>What the run warned about, as the probe would report it, secrets hidden.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>How many actions the run goes through: all of them, or those up to <see cref="UpTo"/>.</summary>
    public int Total => UpTo is null ? FromStart ? 0 : Actions.Count : Actions.TakeWhile(action => action != UpTo).Count() + 1;

    /// <summary>
    /// The recorded actions it did not get to replay, or not pass: those after the last one that passed in a row from the
    /// start, what led to the one that failed included.
    /// </summary>
    public IReadOnlyList<string> NotReplayed(Capture.Recording recording)
    {
        var passed = Actions.TakeWhile(action => Steps.GetValueOrDefault(action)?.State == StepState.Passed).LastOrDefault();
        var ids = recording.Actions.Select(action => action.Id).ToList();
        return [.. ids.Skip(passed is null ? 0 : ids.IndexOf(passed) + 1)];
    }
}
