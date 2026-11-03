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
                AutoChecks = flow.AutoChecks, Login = flow.Login, Replaces = flow.Replaces,
            };
            switch (ConsoleUi.Choose($"Found an unsaved recording of {flow.Flow} ({ActionsIn(recording, flow.Flow)}, " +
                                     $"{ConsoleUi.Ago(changed)})", ["Save it now", "Continue recording", "Discard"]))
            {
                case 0:
                    SaveAsync(request, recording, new Autosave()).GetAwaiter().GetResult();
                    break;
                case 1:
                    return request with { Continue = true };
                default:
                    File.Delete(path);
                    ConsoleUi.Info($"Discarded the recording of {flow.Flow}.");
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
        try
        {
            // A login flow logs in afresh, as in the probe, rather than from the recorder's saved login.
            var options = (request.Login is { } login
                ? new RecordingOptions(request.Url) { LoginFlow = login.Flow, LoadStorageStatePath = null }
                : new RecordingOptions(request.Url)) with { Continues = continued?.Recording };
            var loggingIn = request.Login is not null;
            var session = await RecordingSession.StartAsync(options, news =>
            {
                // The login's own way in and out of the pause is told by LogInAsync.
                if (news is RecordingState state) { if (!Volatile.Read(ref loggingIn)) ConsoleUi.StateChanged(state); }
                else if (news is LoginProgress progress) ConsoleUi.LoginProgressed(progress);
                else if (news is FrozenElements frozen) ConsoleUi.Froze(frozen);
                else ConsoleUi.Captured(news);
            });
            var panel = request.Panel ? await OpenPanelAsync(session, request) : null;
            using var stopWatching = new CancellationTokenSource();
            var watching = autosave.WatchAsync(session.Build, () => panel?.Request ?? request, stopWatching.Token);
            var loginFailure = request.Login is not null ? await LogInAsync(session, request) : null;
            Volatile.Write(ref loggingIn, false);
            if (continued is not null && !await ReplayAsync(session, panel, request, loginFailure))
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
            await stopWatching.CancelAsync();
            await watching;
            if (session.LoginNotSaved is { } problem) ConsoleUi.Warning(problem);
        }
        catch (PlaywrightException failure)
        {
            ConsoleUi.Error($"Could not record: {failure.Message.Split('\n')[0]}");
            return 1;
        }

        if (recording.Actions.Count == 0)
        {
            autosave.Forget();
            ConsoleUi.Error("No actions recorded. Interact with the page before closing.");
            return 1;
        }

        // On disk as it ended, in case saving it fails.
        await autosave.WriteAsync(recording, request);
        return await SaveAsync(request, recording, autosave);
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
                            "  The recording is kept: run qaas-playwright again after updating it to save it.\n" +
                            $"  Please report this, with where it happened:\n{failure.StackTrace}");
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
            ConsoleUi.Error($"Could not save the flow: {failure.Message}\n  The recorded flow is kept in {kept}");
            return 1;
        }

        if (request.KeepRecording) saved = saved with { RecordingPath = await KeepAsync(saved, recording) };
        await autosave.SavedAsync(recording, request, saved, flow.Code);
        ConsoleUi.Saved(request, saved, flow);
        ConsoleUi.Secrets(flow, saved.YamlPath);
        if (Autosave.Unkept(recording, request.ClassName).Except(flow.Secrets.Select(setting => setting.Name)).ToList() is [_, ..] unkept)
            ConsoleUi.Warning($"The recorder keeps no password in its copy of a recording: set {string.Join(", ", unkept)} " +
                              $"in {Path.GetFileName(saved.YamlPath)}.");
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
                ConsoleUi.Error($"{request.ClassName} was recorded after {after}: continue it with --login {after}, in your " +
                                $"test project's folder, built; or add --login-assembly: the DLL in its bin folder that has {after}.");
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
                ConsoleUi.Warning($"{request.CodePath} was changed since it was recorded: the flow continued is saved beside it, " +
                                  "to merge by hand.");
        }

        request = request with { Url = recording.StartUrl, AutoChecks = autoChecks, Replaces = replaces };
        return kept;
    }

    // The steps of the recording continued replay in the recorded window, as a test runs them. When they cannot, or the
    // login flow before them failed, the person keeps recording from where they stopped, without the steps from there
    // on, or stops. Returns false to stop.
    private static async Task<bool> ReplayAsync(RecordingSession session, RecorderPanel? panel, RecordRequest request,
        string? loginFailure)
    {
        if (loginFailure is not null) return false;
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

        if (report.Outcome != TestOutcome.Passed)
        {
            var notReplayed = report.NotReplayed(session.Build());
            var first = report.Actions.TakeWhile(action => report.Steps.GetValueOrDefault(action)?.State == StepState.Passed).Count() + 1;
            var steps = first < report.Actions.Count ? $"Steps {first} to {report.Actions.Count} were" : $"Step {first} was";
            panel?.ShowBanner("The replay stopped: in the console, keep recording from here, or stop.");
            int choice;
            try
            {
                choice = ConsoleUi.Choose($"{steps} not replayed",
                    [$"Keep recording from here, without {(first < report.Actions.Count ? "them" : "it")}",
                        $"Stop, and keep {request.ClassName} as it is"]);
            }
            catch (OperationCanceledException)
            {
                choice = 1;
            }

            if (choice == 1) return false;
            session.Log.Remove(notReplayed);
        }

        panel?.ShowBanner(null);
        ConsoleUi.NumberAfter(session.Build());
        session.EndTest();
        return true;
    }

    // The login flow runs on the page the recording opened on the URL, before anything is recorded. When it fails, or the
    // person stops it (Esc here or in the page, or the panel) or it hangs, the recording stays paused for the person to
    // log in by hand and resume it, or to close the browser. Returns why it did not log in, or null.
    private static async Task<string?> LogInAsync(RecordingSession session, RecordRequest request)
    {
        var login = request.Login!;
        ConsoleUi.LoggingIn(login, request.LoginRemembered);
        using var watching = new CancellationTokenSource();
        var escape = ConsoleUi.EscapePressedAsync(watching.Token);
        _ = escape.ContinueWith(pressed => { if (pressed.Result) session.LogInByHand(); }, TaskScheduler.Default);
        LoginEnd? end;
        try
        {
            end = await session.LogInAsync((page, stopped) => LoginRunner.RunAsync(login, page, request.Url, stopped),
                LoginRunner.Scrubber(login));
        }
        finally
        {
            await watching.CancelAsync();
            await escape;
        }

        var continued = request.Continue ? request.ClassName : null;
        if (end is null) ConsoleUi.LoggedIn(login.Flow, request.Continue ? null : session.Page.Url);
        else if (end.ByHand) ConsoleUi.LoginStopped(end.Why, continued);
        else ConsoleUi.LoginFailed(login.Flow, end.Why, continued);
        return end?.Why;
    }

    // The raw recording, beside the flow and named after it, for a person to share when an element was located badly.
    private static async Task<string?> KeepAsync(SavedFlow saved, Recording recording)
    {
        var path = Path.ChangeExtension(saved.CodePath, ".recording.json");
        try
        {
            await File.WriteAllTextAsync(path, recording.ToJson());
            return path;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            ConsoleUi.Warning($"The recording was not kept: {failure.Message}");
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
            ConsoleUi.Warning($"The panel did not open, recording without it: {failure.Message.Split('\n')[0]}");
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
