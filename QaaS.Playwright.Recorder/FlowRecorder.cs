using System.Diagnostics;
using Microsoft.Playwright;
using QaaS.Playwright.Files;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Generation;
using QaaS.Playwright.Recorder.Login;
using QaaS.Playwright.Recorder.Panel;
using QaaS.Playwright.Recorder.State;
using QaaS.Playwright.Recorder.TestRun;

namespace QaaS.Playwright.Recorder;

/// <summary>The recorder's Chrome and its window, which recording after recording runs in.</summary>
internal sealed record RecorderWindow(RecorderBrowser Browser, PanelHost Host)
{
    /// <summary>The recordings start from the login the recorder saved, and save it as they end; not in its tests.</summary>
    public bool KeepsLogin { get; init; } = true;
}

/// <summary>
/// How a recording ended, for the window to show: whether it started and was saved, the flow and its files, what the
/// console said to do next, and the lines to check by hand; or why it was not saved.
/// </summary>
internal sealed class RecordingOutcome
{
    public bool Started { get; set; } = true;
    public bool Saved { get; set; }
    public string Flow { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<(string Path, string What)> Files { get; } = [];
    public List<string> Notes { get; } = [];
    public List<string> Checks { get; } = [];
    public string? Error { get; set; }
}

/// <summary>The flow's saved files: its class, its recorded values, and the raw recording when it was kept.</summary>
internal sealed record SavedFlow(string CodePath, string YamlPath, string? RecordingPath = null);

/// <summary>
/// Records a flow in Chrome and saves it as a C# flow class with the YAML of its values. A recording kept by
/// <see cref="Autosave"/> can be saved later, or continued: its steps replay in the recorded window, and what is recorded
/// then comes after them.
/// </summary>
internal static class FlowRecorder
{
    /// <summary>
    /// Records in the recorder's window and Chrome, <paramref name="window"/>, which stay open after it; with none, in a
    /// Chrome of its own with a window of its own, which close as the recording ends. Returns the exit code.
    /// </summary>
    public static async Task<int> RecordAsync(RecordRequest request, RecorderWindow? window = null, RecordingOutcome? outcome = null)
    {
        var lifetime = RecorderLifetime.Current = new RecorderLifetime();
        outcome ??= new RecordingOutcome();
        outcome.Flow = request.ClassName;
        try
        {
            return await RecordInAsync(request, lifetime, window, outcome);
        }
        finally
        {
            lifetime.Children.KillAll();
            RecorderLifetime.Current = null;
        }
    }

    // Records until something asks the lifetime to stop, then ends as it says: saved, or kept to save next time.
    private static async Task<int> RecordInAsync(RecordRequest request, RecorderLifetime lifetime, RecorderWindow? window, RecordingOutcome outcome)
    {
        KeptRecording? continued = null;
        if (request.Continue && (continued = Continued(ref request)) is null)
        {
            (outcome.Started, outcome.Error) = (false, $"There is no recording of {request.ClassName} to continue.");
            return 1;
        }
        ConsoleUi.Recording(request, continued is null ? null
            : $"{ActionsIn(continued.Recording, request.ClassName)}, recorded {ConsoleUi.Ago(continued.Changed)}");
        var autosave = new Autosave(continued?.Recording, request);
        RecordingSession? recorded = null;
        RecorderPanel? shown = null;
        RecorderBrowser? ownBrowser = null;
        // However it ends, the recording is first kept as it is then.
        lifetime.OnSnapshot(async _ =>
        {
            if (recorded?.Build() is not { Actions.Count: > 0 } now) return false;
            await autosave.WriteAsync(now, shown?.Request ?? request);
            return true;
        });
        try
        {
            // A login flow logs in afresh, as in the probe, rather than from the recorder's saved login; so does a flow
            // continued that logs in itself, as it was recorded signed out.
            var options = (request.Login is { } login
                ? new RecordingOptions(request.Url) { LoginFlow = login.Flow, LoginBaseUrl = login.BaseUrlFor(request.Url).Url, LoadStorageStatePath = null }
                : new RecordingOptions(request.Url)) with { Continues = continued?.Recording, Lifetime = lifetime };
            // The recorder's Chrome, or one of the recording's own, ended last, after the recording's browser context.
            var browser = window?.Browser ?? (ownBrowser = await RecorderBrowser.LaunchAsync([]));
            if (window is null) lifetime.Own("Chrome", browser.DisposeAsync);
            options = options with { Browser = browser };
            if (window?.KeepsLogin == false) options = options with { LoadStorageStatePath = null, SaveStorageStatePath = null };
            if (continued?.Recording.LogsIn == true) options = options with { LoadStorageStatePath = null };
            var loggingIn = request.Login is not null;
            var session = await RecordingSession.StartAsync(options, news =>
            {
                // The login's own way in and out of the pause is told by LogInAsync.
                if (news is RecordingState state) ConsoleUi.StateChanged(state, say: !Volatile.Read(ref loggingIn));
                else if (news is LoginProgress progress) ConsoleUi.LoginProgressed(progress);
                else if (news is FrozenElements frozen) ConsoleUi.Froze(frozen);
                else if (news is NotRecordedWhilePaused ignored) ConsoleUi.NotRecordedWhilePaused(ignored.What);
                else ConsoleUi.Captured(news);
            });
            recorded = session;
            lifetime.OnDrain(token => session.DrainAsync(session.Pipeline.Epoch, token));
            lifetime.Own("the recording's browser context", session.DisposeAsync);
            // The console numbers each action as the flow, the panel and a test do.
            ConsoleUi.Numbering = () => StepNumbers(session.Build(), (shown?.Request ?? request).ClassName);
            var panel = shown = await OpenPanelAsync(session, request, window?.Host);
            if (panel is not null) lifetime.Own("the panel", panel.DisposeAsync);
            using var stopWatching = new CancellationTokenSource();
            var watching = autosave.WatchAsync(session.Build, () => panel?.Request ?? request, stopWatching.Token);
            var loginFailure = request.Login is not null ? await LogInAsync(session, request) : null;
            Volatile.Write(ref loggingIn, false);
            var replayed = continued is null ? Replayed.All : await ReplayAsync(session, panel, request, loginFailure);
            if (replayed == Replayed.LeftOut)
            {
                // The steps left out are still the flow's: it stays as it is, and what is recorded now is saved beside it.
                request = LeftOut(request);
                panel?.KeepFlowFile();
                ConsoleUi.Warning($"{Path.GetFileName(request.CodePath)} stays as it is, as some of its steps did not replay and " +
                                  "are left out of what you record now. Saving writes the flow continued beside it, as " +
                                  $"{Path.GetFileName(Path.ChangeExtension(request.CodePath, ".recorded.txt"))}: merge the two by hand.");
            }

            if (replayed == Replayed.Stopped)
            {
                await stopWatching.CancelAsync();
                await watching;
                autosave.Forget();
                if (panel is not null) await panel.DisposeAsync();
                await session.DisposeAsync();
                if (ownBrowser is not null) await ownBrowser.DisposeAsync();
                outcome.Error = $"Stopped: {request.ClassName} stays as it is.";
                return 1;
            }

            // What ends the recording asks the lifetime to; so does Ctrl+C, which ends it as Save does, and again stops it at
            // once, the recording kept.
            lifetime.Listening = true;
            _ = session.Closed.ContinueWith(_ => lifetime.RequestStop(StopReason.BrowserClosed), TaskScheduler.Default);
            _ = panel?.SaveRequested.ContinueWith(_ => lifetime.RequestStop(StopReason.Saved), TaskScheduler.Default);
            await lifetime.Stopped;
            return await lifetime.ShutdownAsync(async forced =>
            {
                Recording recording;
                try
                {
                    if (panel is not null)
                    {
                        await panel.DisposeAsync();
                        request = panel.Request;
                    }

                    recording = await session.StopAsync(await lifetime.Stopped);
                    ConsoleUi.Numbering = null;
                    await stopWatching.CancelAsync();
                    await watching;
                    if (session.LoginNotSaved is { } problem) ConsoleUi.Warning(problem);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    return await KeptAsync(failure);
                }

                // Forced meanwhile, the recording is kept rather than written.
                forced.ThrowIfCancellationRequested();
                return await SaveEndedAsync(request, recording, autosave, outcome);
            });
        }
        catch (PlaywrightException failure) when (recorded?.Log.Build("").Actions.Count is not > 0)
        {
            if (recorded is not null) await recorded.DisposeAsync();
            if (ownBrowser is not null) await ownBrowser.DisposeAsync();
            var why = $"Could not record: {failure.Message.Split('\n')[0]}. Chrome did not open, or the page did not load: check " +
                      "that the address answers in your browser, then start again.";
            ConsoleUi.Error(why);
            (outcome.Started, outcome.Error) = (false, why);
            return 1;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return await KeptAsync(failure);
        }

        // Chrome or its driver went away, or a recorder bug: what was recorded is kept, to save or continue it next time.
        async Task<int> KeptAsync(Exception failure)
        {
            try
            {
                await autosave.WriteAsync(recorded!.Log.Build(request.Url), request);
            }
            catch (Exception notKept) when (notKept is not OutOfMemoryException)
            {
                // The autosave so far stays.
            }

            CrashReport.Report(failure);
            outcome.Error = $"The recorder stopped on an error ({failure.GetType().Name}: {failure.Message.Split('\n')[0]}). The " +
                            "recording is kept: it is offered under Recovered.";
            return 1;
        }
    }

    // The recording ended as asked: nothing to save, or the flow written from it.
    private static async Task<int> SaveEndedAsync(RecordRequest request, Recording recording, Autosave autosave, RecordingOutcome outcome)
    {
        if (recording.Actions.Count == 0)
        {
            autosave.Forget();
            outcome.Error = "Nothing was recorded, so nothing was saved: Chrome was closed, or Save pressed, before any action. " +
                            "Start again, then click and type in the app before you save.";
            ConsoleUi.Error(outcome.Error);
            return 1;
        }

        // On disk as it ended, in case saving it fails.
        await autosave.WriteAsync(recording, request);
        return await SaveAsync(request, recording, autosave, outcome);
    }

    /// <summary>Saves a recording that never ended in a save, now, without Chrome, as Save it now asks. Returns the exit code.</summary>
    public static Task<int> SaveKeptAsync(KeptRecording kept, RecordingOutcome outcome)
    {
        var flow = kept.Recording.Autosave!;
        outcome.Flow = flow.Flow;
        return SaveAsync(new RecordRequest(flow.Flow, kept.Recording.StartUrl, flow.Folder)
        {
            AutoChecks = flow.AutoChecks, Login = flow.Login, Replaces = flow.Replaces, KeepRecording = flow.KeepRecording,
        }, kept.Recording, new Autosave(), outcome);
    }

    // Each action the flow does by its step number, as the panel and a test number them: a click that only opened a
    // list, or did nothing, has none.
    private static IReadOnlyDictionary<string, int> StepNumbers(Recording recording, string flow)
    {
        try
        {
            return FlowCodeGenerator.PlanIsolated(recording, flow).Steps.SelectMany(step => step.Actions).Where(action => action.Kind != "expect")
                .Select(action => action.Source.Id).Distinct().Select((id, index) => (id, index + 1)).ToDictionary();
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // A recording the planner cannot plan yet is numbered as it comes.
            return recording.Actions.Select((action, index) => (action.Id, index + 1)).DistinctBy(pair => pair.Id).ToDictionary();
        }
    }

    // "12 actions", as the flow does them and its summary counts them once saved; as recorded when the flow cannot be
    // written, so a recording is still offered, and can be discarded, whatever it holds.
    private static string ActionsIn(Recording recording, string flow)
    {
        int count;
        try
        {
            count = FlowCodeGenerator.Generate(recording, flow, "Flows").ActionLines.Count;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            count = recording.Actions.Count;
        }

        return count == 1 ? "1 action" : $"{count} actions";
    }

    // Writes the flow and its values, says what was saved, and keeps the recording to continue it. Returns the exit code.
    private static async Task<int> SaveAsync(RecordRequest request, Recording recording, Autosave autosave, RecordingOutcome? outcome = null)
    {
        outcome ??= new RecordingOutcome();
        GeneratedFlow flow;
        try
        {
            flow = FlowCodeGenerator.Generate(recording, request.ClassName, ProjectNamespace.Of(request.OutputDir),
                request.AutoChecks);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // A recorder bug, not the person's: the recording stays kept, to save once the recorder is fixed, and where it
            // happened is logged, to report it.
            var logged = CrashReport.Log($"Could not write the flow {request.ClassName}", failure);
            outcome.Error = $"Could not write the flow {request.ClassName}: {failure.GetType().Name}: {failure.Message}. This is a " +
                            $"bug of the recorder; the recording is kept, under Recovered. Please report it with {CrashReport.LogPath}.";
            ConsoleUi.Error($"Could not write the flow {request.ClassName}: {failure.GetType().Name}: {failure.Message}\n" +
                            $"  This is a bug of the recorder, not of what you did. The recording is kept in {Autosave.Folder}: " +
                            "update the recorder (dotnet tool update -g QaaS.Playwright.Recorder), then run qaas-playwright: " +
                            "it is offered under Recovered.\n" +
                            (logged
                                ? $"  Please report it with {CrashReport.LogPath}, which says where it happened."
                                : $"  Please report it, with where it happened:\n{failure.StackTrace}"));
            return 1;
        }

        SavedFlow saved;
        try
        {
            saved = Save(request, flow);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Recording it again would take as long as the first time; the recorder offers it again when it starts.
            var kept = Path.Combine(Path.GetTempPath(), $"{request.ClassName}-{Guid.NewGuid():N}.cs.txt");
            FileStore.Machine.Write(kept, flow.Code + "\n/*\n" + flow.Yaml + "*/\n");
            outcome.Error = $"Could not save the flow: {failure.Message}. The folder may be read-only, or a file in it open in " +
                            $"another program. The recorded flow is kept in {kept}.";
            ConsoleUi.Error($"Could not save the flow: {failure.Message}\n  The folder may be read-only, or a file in it " +
                            $"open in another program. The recorded flow is kept in {kept}: copy it into your project, or " +
                            "free the folder and save it from Recovered, on the start screen.");
            return 1;
        }

        if (request.KeepRecording) saved = saved with { RecordingPath = Keep(saved, recording) };
        await autosave.SavedAsync(recording, request, saved, flow.Code);
        ConsoleUi.Saved(request, saved, flow);
        ConsoleUi.Secrets(flow, saved.YamlPath);
        if (Autosave.Unkept(recording, request.ClassName).Except(flow.Secrets.Select(setting => setting.Name)).ToList() is [_, ..] unkept)
            ConsoleUi.Warning($"{string.Join(", ", unkept)} {(unkept.Count == 1 ? "is" : "are")} empty in " +
                              $"{Path.GetFileName(saved.YamlPath)}: the recorder never keeps a password in its copy of a " +
                              $"recording, so it could not fill {(unkept.Count == 1 ? "it" : "them")} in. Type the value there, " +
                              "or set its environment variable, before you run the flow.");
        if (request.Login is { } after) ConsoleUi.RunAfter(after.Flow);
        Told(outcome, request, saved, flow, recording);
        return 0;
    }

    // What the window shows of a flow saved: its files, what to do next, and what to check by hand.
    private static void Told(RecordingOutcome outcome, RecordRequest request, SavedFlow saved, GeneratedFlow flow, Recording recording)
    {
        outcome.Saved = true;
        outcome.Flow = request.ClassName;
        outcome.Summary = $"{ConsoleUi.Count(flow.StepLines.Count, "step")}, {ConsoleUi.Count(flow.ActionLines.Count, "action")}" +
                          (flow.Checks.Count > 0 ? $", {flow.Checks.Count} to check by hand" : ", nothing to check by hand");
        outcome.Files.Add((saved.CodePath, saved.CodePath.EndsWith(".cs", StringComparison.Ordinal) ? "the flow" : "the new recording, beside the flow"));
        outcome.Files.Add((saved.YamlPath, "the values it types and chooses"));
        if (saved.RecordingPath is { } raw) outcome.Files.Add((raw, "the raw recording"));
        if (!saved.CodePath.EndsWith(".cs", StringComparison.Ordinal))
            outcome.Notes.Add($"{request.ClassName}.cs was kept as it is, as it changed since it was recorded: merge the new recording, saved beside it, into it by hand.");
        if (flow.Secrets.Count > 0)
            outcome.Notes.Add($"{string.Join(", ", flow.Secrets.Select(setting => setting.Name))}: no file of the flow holds a secret. " +
                              $"{Path.GetFileName(saved.YamlPath)} reads {string.Join(", ", flow.Secrets.Select(setting => FlowYaml.EnvironmentName(setting.Name)))} from the environment.");
        if (Autosave.Unkept(recording, request.ClassName).Except(flow.Secrets.Select(setting => setting.Name)).ToList() is [_, ..] unkept)
            outcome.Notes.Add($"{string.Join(", ", unkept)} {(unkept.Count == 1 ? "is" : "are")} empty in {Path.GetFileName(saved.YamlPath)}: " +
                              "type the value there, or set its environment variable, before you run the flow.");
        if (request.Login is { } after)
            outcome.Notes.Add($"It runs after {after.Flow}: in your test YAML, SetupFlows: [{after.Flow}] goes before it.");
        outcome.Checks.AddRange(flow.Checks.Select(check => check.Line is { } line ? $"Line {line}: {check.Message}" : check.Message));
    }

    // The recording to go on from, with the flow's login and its values the recording does not hold; null, told why,
    // when there is none. Saving replaces the flow's file only when it is as recorded: else the person changed it since.
    private static KeptRecording? Continued(ref RecordRequest request)
    {
        if (Autosave.Find(request.OutputDir, request.ClassName) is not { } kept)
        {
            ConsoleUi.NothingToContinue(request);
            return null;
        }

        var recording = kept.Recording;
        var flow = recording.Autosave;
        if (recording.After is { } after && request.Login?.Flow != after)
        {
            if (flow?.Login?.Flow != after)
            {
                ConsoleUi.Error($"{request.ClassName} was recorded after the login flow {after}, and the recorder cannot find " +
                                $"{after} here, so its steps would replay signed out. Choose the test project that has {after}, " +
                                "built (dotnet build), on the start screen; Why no login? there shows where the recorder looked.");
                return null;
            }

            request = request with { Login = flow.Login, LoginRemembered = false };
        }

        var autoChecks = flow?.AutoChecks ?? request.AutoChecks;
        Autosave.Unkept(recording, request.ClassName, Path.ChangeExtension(request.CodePath, ".flow.yaml"));
        string? replaces = null;
        if (File.Exists(request.CodePath))
        {
            var now = Autosave.Hash(File.ReadAllText(request.CodePath));
            var recorded = FlowCodeGenerator.Generate(recording, request.ClassName, ProjectNamespace.Of(request.OutputDir), autoChecks);
            replaces = now == Autosave.Hash(recorded.Code) || now == flow?.Replaces ? now : null;
            if (replaces is null)
                ConsoleUi.Warning($"{request.CodePath} was changed by hand since it was recorded, so saving will not overwrite " +
                                  "it: the flow continued is saved beside it. Merge the two by hand once you saved.");
        }

        request = request with { Url = recording.StartUrl, AutoChecks = autoChecks, Replaces = replaces };
        return kept;
    }

    /// <summary>
    /// The request of a flow continued without the steps that did not replay: saving never replaces the flow, which has
    /// them still, but writes beside it, as for a flow changed by hand.
    /// </summary>
    internal static RecordRequest LeftOut(RecordRequest request) => request with { Replaces = null };

    internal enum Replayed { All, LeftOut, Stopped }

    // The steps of the recording continued replay in the recorded window, as a test runs them. When they cannot, or the
    // login flow before them failed, the person keeps recording from where they stopped, without the steps from there
    // on, or stops. Saved, or Chrome closed, while they replay, which stops them, the flow is saved as it was.
    internal static async Task<Replayed> ReplayAsync(RecordingSession session, RecorderPanel? panel, RecordRequest request,
        string? loginFailure, Func<string, IReadOnlyList<string>, int>? choose = null)
    {
        if (loginFailure is not null) return Replayed.Stopped;
        var tester = panel?.Tester ?? new FlowTester(session);
        // The replay is the state the recording started in, or went to once the login flow logged in.
        var run = session.Snapshot.Run;
        TestReport report;
        try
        {
            panel?.ShowBanner($"Replaying the steps of {request.ClassName}: nothing is recorded until they ran.");
            report = await tester.ReplayAsync(request, ProjectNamespace.Of(request.OutputDir));
        }
        finally
        {
            if (panel is null) await tester.DisposeAsync();
        }

        var ending = session.Closed.IsCompleted || session.Page.IsClosed || panel?.SaveRequested.IsCompleted == true;
        if (!ending && run is not null) await session.RequestAsync(new RunEnded(run.Id, FlowTester.OutcomeOf(report)));
        if (report.Outcome != TestOutcome.Passed && !ending)
        {
            var notReplayed = report.NotReplayed(session.Build());
            var first = report.Actions.TakeWhile(action => report.Steps.GetValueOrDefault(action)?.State == StepState.Passed).Count() + 1;
            var steps = first < report.Actions.Count ? $"Steps {first} to {report.Actions.Count} were" : $"Step {first} was";
            var question = $"{steps} not replayed: the page did not do what it did when you recorded {request.ClassName} (the " +
                           "panel and the lines above say why).";
            int choice;
            // A test answers for the person; with no window to ask in, the flow stays as it is.
            if (choose is not null || panel is null)
                choice = choose?.Invoke($"{question} What should the recorder do?",
                    [$"Keep recording from here, without {(first < report.Actions.Count ? "those steps" : "that step")}; the flow file stays as it is",
                        $"Stop, and keep {request.ClassName} as it is"]) ?? 1;
            else
            {
                // The panel asks: keep recording from here, or stop; saving, or Chrome closed, saves the flow as it was.
                ConsoleUi.Warning($"{question} In the window: keep recording from here, without them, or stop.");
                var answered = await Task.WhenAny(panel.KeepRecordingAnswered, panel.SaveRequested, session.Closed);
                if (answered != panel.KeepRecordingAnswered) return Replayed.All;
                choice = await panel.KeepRecordingAnswered ? 0 : 1;
            }

            if (choice == 1) return Replayed.Stopped;
            session.Log.Remove(notReplayed);
            await session.RequestAsync(new KeepRecording(true));
        }

        panel?.ShowBanner(null);
        ConsoleUi.NumberAfter(session.Build());
        return report.Outcome == TestOutcome.Passed || ending ? Replayed.All : Replayed.LeftOut;
    }

    // The login flow runs first, on a page that loaded nothing yet, with the BaseUrl the tests give it, before anything is
    // recorded; then the page goes to the URL. When it fails, or the person stops it (Esc in the page, or the panel) or
    // it hangs, the recording stays paused for the person to log in by hand and resume it, or to close the
    // browser. Returns why it did not log in, or null.
    internal static async Task<string?> LogInAsync(RecordingSession session, RecordRequest request)
    {
        var login = request.Login!;
        var (baseUrl, baseUrlFrom) = login.BaseUrlFor(request.Url);
        ConsoleUi.LoggingIn(login, request.LoginRemembered, baseUrl, baseUrlFrom);
        // Esc in the app, or Log in by hand in the window, stops it.
        var end = await session.LogInAsync((page, stopped) => LoginRunner.RunAsync(login, page, baseUrl, stopped, baseUrlFrom),
            LoginRunner.Scrubber(login));

        var continued = request.Continue ? request.ClassName : null;
        if (end is null) ConsoleUi.LoggedIn(login.Flow, request.Continue ? null : session.Page.Url, session.StartedAfterLogin);
        else if (end.ByHand)
            ConsoleUi.LoginStopped(end.Why.StartsWith("You stopped", StringComparison.Ordinal)
                ? end.Why
                : $"{end.Why} It was given BaseUrl {baseUrl} ({(baseUrlFrom is null ? "the address you record on" : $"from {ConsoleUi.Relative(baseUrlFrom)}")}): " +
                  $"if that is not your app's root, set BaseUrl in the test YAML that runs {login.Flow}.", continued);
        else ConsoleUi.LoginFailed(login.Flow, end.Why, continued);
        return end?.Why;
    }

    // The raw recording, beside the flow and named after it, for a person to share when an element was located badly:
    // without its secrets, as it may well be shared.
    private static string? Keep(SavedFlow saved, Recording recording)
    {
        var path = Path.ChangeExtension(saved.CodePath, ".recording.json");
        try
        {
            FileStore.Machine.Write(path, Autosave.WithoutSecrets(recording).ToJson());
            return path;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            ConsoleUi.Warning($"The raw recording was not written beside the flow ({failure.Message}); the flow itself is " +
                              "saved. Check that the folder can be written to, if you need the raw recording.");
            return null;
        }
    }

    // The panel helps, but the recording does not need it.
    private static async Task<RecorderPanel?> OpenPanelAsync(RecordingSession session, RecordRequest request, PanelHost? host)
    {
        try
        {
            return await RecorderPanel.OpenAsync(session, request, host: host);
        }
        catch (PlaywrightException failure)
        {
            ConsoleUi.Warning($"The panel did not open ({failure.Message.Split('\n')[0]}), so this recording goes on without " +
                              "it: each action shows here instead. Save it by closing the app's window.");
            return null;
        }
    }

    // A flow may have been edited by hand since it was recorded, and so may an earlier re-recording, so nothing is
    // overwritten but the file a request replaces, unchanged since: a re-recording is saved beside the flow as
    // <Name>.recorded.txt, then <Name>.recorded-2.txt and so on, to merge by hand, each with its values beside it. Not
    // as .cs, which would declare the flow's class twice and break the project's build.
    // The flow and its YAML are written together or not at all, and a pair a crash left half replaced is put back first.
    internal static SavedFlow Save(RecordRequest request, GeneratedFlow flow)
    {
        var store = FileStore.Machine;
        var flowPath = Path.GetFullPath(request.CodePath);
        store.Recover(Path.GetDirectoryName(flowPath)!);
        // The flow continued, or the one the person chose to overwrite, as it was then.
        if (request.Replaces is { } replaced && File.Exists(flowPath) && Autosave.Hash(File.ReadAllText(flowPath)) == replaced)
        {
            var yamlPath = Path.ChangeExtension(flowPath, ".flow.yaml");
            store.ReplaceTogether([(flowPath, flow.Code), (yamlPath, flow.Yaml)]);
            return new SavedFlow(flowPath, yamlPath);
        }

        foreach (var (code, yaml) in PathsFor(flowPath))
            if (store.CreateTogether([(code, flow.Code), (yaml, flow.Yaml)]))
                return new SavedFlow(code, yaml);

        throw new UnreachableException("There is always another file name to try.");
    }

    private static IEnumerable<(string Code, string Yaml)> PathsFor(string flowPath)
    {
        yield return (flowPath, Path.ChangeExtension(flowPath, ".flow.yaml"));
        yield return (Path.ChangeExtension(flowPath, ".recorded.txt"), Path.ChangeExtension(flowPath, ".recorded.flow.yaml"));
        for (var copy = 2; ; copy++)
            yield return (Path.ChangeExtension(flowPath, $".recorded-{copy}.txt"),
                Path.ChangeExtension(flowPath, $".recorded-{copy}.flow.yaml"));
    }
}
