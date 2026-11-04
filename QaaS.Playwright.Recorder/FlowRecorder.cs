using System.Diagnostics;
using Microsoft.Playwright;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Generation;
using QaaS.Playwright.Recorder.Login;
using QaaS.Playwright.Recorder.Panel;
using QaaS.Playwright.Recorder.TestRun;

namespace QaaS.Playwright.Recorder;

/// <summary>The flow's saved files: its class, its recorded values, and the raw recording when it was kept.</summary>
internal sealed record SavedFlow(string CodePath, string YamlPath, string? RecordingPath = null);

/// <summary>
/// Records a flow in Chrome and saves it as a C# flow class with the YAML of its values. A recording kept by
/// <see cref="Autosave"/> can be saved later, or continued: its steps replay in the recorded window, and what is recorded
/// then comes after them.
/// </summary>
internal static class FlowRecorder
{
    /// <summary>Returns the process exit code: 0 when a flow was saved.</summary>
    public static int Record(RecordRequest request) => RecordAsync(request).GetAwaiter().GetResult();

    /// <summary>
    /// Offers each recording that did not end in a save, newest first: saves it now, without a browser; continues it,
    /// returning the request to; or discards it. Null when none is to continue.
    /// </summary>
    /// <exception cref="OperationCanceledException">The input ended.</exception>
    public static RecordRequest? OfferNotSaved()
    {
        foreach (var (recording, changed, path) in Autosave.NotSaved())
        {
            var flow = recording.Autosave!;
            var request = new RecordRequest(flow.Flow, recording.StartUrl, flow.Folder)
            {
                AutoChecks = flow.AutoChecks, Login = flow.Login, Replaces = flow.Replaces, KeepRecording = flow.KeepRecording,
            };
            switch (ConsoleUi.Choose($"Found a recording of {flow.Flow} that was never saved ({ActionsIn(recording, flow.Flow)}, " +
                                     $"{ConsoleUi.Ago(changed)}): the recorder stopped before you saved it. What should it do with it?",
                        ["Save it now as a flow", "Continue recording it: replay its steps, then record more", "Discard it for good"]))
            {
                case 0:
                    SaveAsync(request, recording, new Autosave()).GetAwaiter().GetResult();
                    break;
                case 1:
                    return request with { Continue = true };
                default:
                    File.Delete(path);
                    ConsoleUi.Info($"Discarded the unsaved recording of {flow.Flow}; nothing else changed.");
                    break;
            }
        }

        return null;
    }

    private static async Task<int> RecordAsync(RecordRequest request)
    {
        KeptRecording? continued = null;
        if (request.Continue && (continued = Continued(ref request)) is null) return 1;
        ConsoleUi.Recording(request, continued is null ? null
            : $"{ActionsIn(continued.Recording, request.ClassName)}, recorded {ConsoleUi.Ago(continued.Changed)}");
        var autosave = new Autosave(continued?.Recording, request);
        Recording recording;
        RecordingSession? recorded = null;
        RecorderPanel? shown = null;
        // An end nobody asked for, an error or a signal from outside, keeps the recording as it is then.
        using var keeping = CrashReport.Keeping(() => recorded is { } now ? autosave.WriteAsync(now.Build(), shown?.Request ?? request) : Task.CompletedTask);
        try
        {
            // A login flow logs in afresh, as in the probe, rather than from the recorder's saved login; so does a flow
            // continued that logs in itself, as it was recorded signed out.
            var options = (request.Login is { } login
                ? new RecordingOptions(request.Url) { LoginFlow = login.Flow, LoginBaseUrl = login.BaseUrlFor(request.Url).Url, LoadStorageStatePath = null }
                : new RecordingOptions(request.Url)) with { Continues = continued?.Recording };
            if (continued?.Recording.LogsIn == true) options = options with { LoadStorageStatePath = null };
            var loggingIn = request.Login is not null;
            var session = await RecordingSession.StartAsync(options, news =>
            {
                // The login's own way in and out of the pause is told by LogInAsync.
                if (news is RecordingState state) { if (!Volatile.Read(ref loggingIn)) ConsoleUi.StateChanged(state); }
                else if (news is LoginProgress progress) ConsoleUi.LoginProgressed(progress);
                else if (news is FrozenElements frozen) ConsoleUi.Froze(frozen);
                else ConsoleUi.Captured(news);
            });
            recorded = session;
            // The console numbers each action as the flow, the panel and a test do.
            ConsoleUi.Numbering = () => StepNumbers(session.Build(), (shown?.Request ?? request).ClassName);
            var panel = shown = request.Panel ? await OpenPanelAsync(session, request) : null;
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
                return 1;
            }

            await Task.WhenAny(session.Closed, ConsoleUi.EnterPressedAsync(), panel?.SaveRequested ?? session.Closed);
            if (panel is not null)
            {
                await panel.DisposeAsync();
                request = panel.Request;
            }

            recording = await session.StopAsync();
            ConsoleUi.Numbering = null;
            await stopWatching.CancelAsync();
            await watching;
            if (session.LoginNotSaved is { } problem) ConsoleUi.Warning(problem);
        }
        catch (PlaywrightException failure) when (recorded?.Log.Build("").Actions.Count is not > 0)
        {
            ConsoleUi.Error($"Could not record: {failure.Message.Split('\n')[0]}\nChrome did not open or the page did not load. " +
                            "Check that Google Chrome is installed and the address answers in your browser, then run the same " +
                            "command again.");
            return 1;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // Chrome or its driver went away, or a recorder bug: what was recorded is kept, to save or continue it next time.
            try
            {
                await autosave.WriteAsync(recorded!.Log.Build(request.Url), request);
            }
            catch (Exception notKept) when (notKept is not OutOfMemoryException)
            {
                // The autosave so far stays.
            }

            CrashReport.Report(failure);
            return 1;
        }

        if (recording.Actions.Count == 0)
        {
            autosave.Forget();
            ConsoleUi.Error("Nothing was recorded, so nothing was saved: Chrome was closed, or Enter pressed, before any " +
                            "action. Run the same command again, then click and type in the page before you close it.");
            return 1;
        }

        // On disk as it ended, in case saving it fails.
        await autosave.WriteAsync(recording, request);
        return await SaveAsync(request, recording, autosave);
    }

    // Each action the flow does by its step number, as the panel and a test number them: a click that only opened a
    // list, or did nothing, has none.
    private static IReadOnlyDictionary<string, int> StepNumbers(Recording recording, string flow)
    {
        try
        {
            return FlowPlanner.Plan(recording, flow).Steps.SelectMany(step => step.Actions).Where(action => action.Kind != "expect")
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
    private static async Task<int> SaveAsync(RecordRequest request, Recording recording, Autosave autosave)
    {
        GeneratedFlow flow;
        try
        {
            flow = FlowCodeGenerator.Generate(recording, request.ClassName, ProjectNamespace.Of(request.OutputDir),
                request.AutoChecks);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // A recorder bug, not the person's: the recording stays kept, to save once the recorder is fixed.
            ConsoleUi.Error($"Could not write the flow {request.ClassName}: {failure.GetType().Name}: {failure.Message}\n" +
                            "  This is a bug of the recorder, not of what you did. The recording is kept: update the recorder " +
                            "(dotnet tool update -g QaaS.Playwright.Recorder), then run qaas-playwright to save it.\n" +
                            $"  Please report it, with where it happened:\n{failure.StackTrace}");
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
            await File.WriteAllTextAsync(kept, flow.Code + "\n/*\n" + flow.Yaml + "*/\n");
            ConsoleUi.Error($"Could not save the flow: {failure.Message}\n  The folder may be read-only, or a file in it " +
                            $"open in another program. The recorded flow is kept in {kept}: copy it into your project, or " +
                            "free the folder and run qaas-playwright, which offers to save it again.");
            return 1;
        }

        if (request.KeepRecording) saved = saved with { RecordingPath = await KeepAsync(saved, recording) };
        await autosave.SavedAsync(recording, request, saved, flow.Code);
        ConsoleUi.Saved(request, saved, flow);
        ConsoleUi.Secrets(flow, saved.YamlPath);
        if (Autosave.Unkept(recording, request.ClassName).Except(flow.Secrets.Select(setting => setting.Name)).ToList() is [_, ..] unkept)
            ConsoleUi.Warning($"{string.Join(", ", unkept)} {(unkept.Count == 1 ? "is" : "are")} empty in " +
                              $"{Path.GetFileName(saved.YamlPath)}: the recorder never keeps a password in its copy of a " +
                              $"recording, so it could not fill {(unkept.Count == 1 ? "it" : "them")} in. Type the value there, " +
                              "or set its environment variable, before you run the flow.");
        if (request.Login is { } after) ConsoleUi.RunAfter(after.Flow);
        return 0;
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
                                $"{after} here, so its steps would replay signed out. Continue it with --login {after}, in your " +
                                $"test project's folder, built (dotnet build); or add --login-assembly with the DLL in its bin " +
                                $"folder that has {after}. To see where the recorder looked: qaas-playwright --login-why");
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
        if (report.Outcome != TestOutcome.Passed && !ending)
        {
            var notReplayed = report.NotReplayed(session.Build());
            var first = report.Actions.TakeWhile(action => report.Steps.GetValueOrDefault(action)?.State == StepState.Passed).Count() + 1;
            var steps = first < report.Actions.Count ? $"Steps {first} to {report.Actions.Count} were" : $"Step {first} was";
            panel?.ShowBanner("The replay stopped: in the console, keep recording from here, or stop.");
            int choice;
            try
            {
                choice = (choose ?? ConsoleUi.Choose)($"{steps} not replayed: the page did not do what it did when you recorded " +
                                          $"{request.ClassName} (the panel and the lines above say why). What should the recorder do?",
                    [$"Keep recording from here, without {(first < report.Actions.Count ? "those steps" : "that step")}; the flow file stays as it is",
                        $"Stop, and keep {request.ClassName} as it is"]);
            }
            catch (OperationCanceledException)
            {
                choice = 1;
            }

            if (choice == 1) return Replayed.Stopped;
            session.Log.Remove(notReplayed);
        }

        panel?.ShowBanner(null);
        ConsoleUi.NumberAfter(session.Build());
        session.EndTest();
        return report.Outcome == TestOutcome.Passed || ending ? Replayed.All : Replayed.LeftOut;
    }

    // The login flow runs first, on a page that loaded nothing yet, with the BaseUrl the tests give it, before anything is
    // recorded; then the page goes to the URL. When it fails, or the person stops it (Esc here or in the page, or the
    // panel) or it hangs, the recording stays paused for the person to log in by hand and resume it, or to close the
    // browser. Returns why it did not log in, or null.
    internal static async Task<string?> LogInAsync(RecordingSession session, RecordRequest request)
    {
        var login = request.Login!;
        var (baseUrl, baseUrlFrom) = login.BaseUrlFor(request.Url);
        ConsoleUi.LoggingIn(login, request.LoginRemembered, baseUrl, baseUrlFrom);
        using var watching = new CancellationTokenSource();
        var escape = ConsoleUi.EscapePressedAsync(watching.Token);
        _ = escape.ContinueWith(pressed => { if (pressed.Result) session.LogInByHand(); }, TaskScheduler.Default);
        LoginEnd? end;
        try
        {
            end = await session.LogInAsync((page, stopped) => LoginRunner.RunAsync(login, page, baseUrl, stopped, baseUrlFrom),
                LoginRunner.Scrubber(login));
        }
        finally
        {
            await watching.CancelAsync();
            await escape;
        }

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
    private static async Task<string?> KeepAsync(SavedFlow saved, Recording recording)
    {
        var path = Path.ChangeExtension(saved.CodePath, ".recording.json");
        try
        {
            await File.WriteAllTextAsync(path, Autosave.WithoutSecrets(recording).ToJson());
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
    private static async Task<RecorderPanel?> OpenPanelAsync(RecordingSession session, RecordRequest request)
    {
        try
        {
            return await RecorderPanel.OpenAsync(session, request);
        }
        catch (PlaywrightException failure)
        {
            ConsoleUi.Warning($"The panel did not open ({failure.Message.Split('\n')[0]}), so this recording goes on without " +
                              "it: each action shows here instead. To try the panel again, record again; --no-panel skips it.");
            return null;
        }
    }

    // A flow may have been edited by hand since it was recorded, and so may an earlier re-recording, so nothing is
    // overwritten but the file a request replaces, unchanged since: a re-recording is saved beside the flow as
    // <Name>.recorded.txt, then <Name>.recorded-2.txt and so on, to merge by hand, each with its values beside it. Not
    // as .cs, which would declare the flow's class twice and break the project's build.
    internal static SavedFlow Save(RecordRequest request, GeneratedFlow flow)
    {
        Directory.CreateDirectory(request.OutputDir);
        var flowPath = Path.GetFullPath(request.CodePath);
        // The flow continued, or the one the person chose to overwrite, as it was then.
        if (request.Replaces is { } replaced && File.Exists(flowPath) && Autosave.Hash(File.ReadAllText(flowPath)) == replaced)
        {
            var yamlPath = Path.ChangeExtension(flowPath, ".flow.yaml");
            File.WriteAllText(flowPath, flow.Code);
            File.WriteAllText(yamlPath, flow.Yaml);
            return new SavedFlow(flowPath, yamlPath);
        }

        foreach (var (code, yaml) in PathsFor(flowPath))
        {
            if (File.Exists(code) || File.Exists(yaml) || !TryCreate(code, flow.Code)) continue;
            if (TryCreate(yaml, flow.Yaml)) return new SavedFlow(code, yaml);
            File.Delete(code);
        }

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

    // Only as a new file, so one that appeared since is not overwritten either.
    private static bool TryCreate(string path, string text)
    {
        try
        {
            using var file = new StreamWriter(new FileStream(path, FileMode.CreateNew));
            file.Write(text);
            return true;
        }
        catch (IOException) when (File.Exists(path))
        {
            return false;
        }
    }
}
