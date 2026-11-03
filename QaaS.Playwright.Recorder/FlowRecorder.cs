using System.Diagnostics;
using Microsoft.Playwright;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Generation;
using QaaS.Playwright.Recorder.Login;
using QaaS.Playwright.Recorder.Panel;

namespace QaaS.Playwright.Recorder;

/// <summary>The flow's saved files: its class, its recorded values, and the raw recording when it was kept.</summary>
internal sealed record SavedFlow(string CodePath, string YamlPath, string? RecordingPath = null);

/// <summary>Records a flow in Chrome and saves it as a C# flow class with the YAML of its values.</summary>
internal static class FlowRecorder
{
    /// <summary>Returns the process exit code: 0 when a flow was saved.</summary>
    public static int Record(RecordRequest request) => RecordAsync(request).GetAwaiter().GetResult();

    private static async Task<int> RecordAsync(RecordRequest request)
    {
        ConsoleUi.Recording(request);
        ConsoleUi.RecorderKeys();
        Recording recording;
        try
        {
            // A login flow logs in afresh, as in the probe, rather than from the recorder's saved login.
            var options = request.Login is { } login
                ? new RecordingOptions(request.Url) { LoginFlow = login.Flow, LoadStorageStatePath = null }
                : new RecordingOptions(request.Url);
            var loggingIn = request.Login is not null;
            var session = await RecordingSession.StartAsync(options, news =>
            {
                // The login's own way in and out of the pause is told by LogInAsync.
                if (news is RecordingState state) { if (!Volatile.Read(ref loggingIn)) ConsoleUi.StateChanged(state); }
                else if (news is FrozenElements frozen) ConsoleUi.Froze(frozen);
                else ConsoleUi.Captured(news);
            });
            var panel = request.Panel ? await OpenPanelAsync(session, request) : null;
            if (request.Login is not null) await LogInAsync(session, request);
            Volatile.Write(ref loggingIn, false);
            await Task.WhenAny(session.Closed, ConsoleUi.EnterPressedAsync(), panel?.SaveRequested ?? session.Closed);
            if (panel is not null)
            {
                await panel.DisposeAsync();
                request = panel.Request;
            }

            recording = await session.StopAsync();
            if (session.LoginNotSaved is { } problem) ConsoleUi.Warning(problem);
        }
        catch (PlaywrightException failure)
        {
            ConsoleUi.Error($"Could not record: {failure.Message.Split('\n')[0]}");
            return 1;
        }

        if (recording.Actions.Count == 0)
        {
            ConsoleUi.Error("No actions recorded. Interact with the page before closing.");
            return 1;
        }

        var flow = FlowCodeGenerator.Generate(recording, request.ClassName, ProjectNamespace.Of(request.OutputDir));
        SavedFlow saved;
        try
        {
            saved = Save(request, flow);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Recording it again would take as long as the first time.
            var kept = Path.Combine(Path.GetTempPath(), $"{request.ClassName}-{Guid.NewGuid():N}.cs.txt");
            await File.WriteAllTextAsync(kept, flow.Code + "\n/*\n" + flow.Yaml + "*/\n");
            ConsoleUi.Error($"Could not save the flow: {failure.Message}\n  The recorded flow is kept in {kept}");
            return 1;
        }

        if (request.KeepRecording) saved = saved with { RecordingPath = await KeepAsync(saved, recording) };
        ConsoleUi.Saved(request, saved, flow);
        if (request.Login is { } after) ConsoleUi.RunAfter(after.Flow);
        return 0;
    }

    // The login flow runs on the page the recording opened on the URL, before anything is recorded. When it fails, the
    // recording stays paused for the person to log in by hand and resume it, or to close the browser.
    private static async Task LogInAsync(RecordingSession session, RecordRequest request)
    {
        var login = request.Login!;
        ConsoleUi.LoggingIn(login, request.LoginRemembered);
        if (await session.LogInAsync(page => LoginRunner.RunAsync(login, page, request.Url)) is { } failure)
            ConsoleUi.LoginFailed(login.Flow, failure);
        else
            ConsoleUi.LoggedIn(login.Flow, session.Page.Url);
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

    // A flow may have been edited by hand since it was recorded, and so may an earlier re-recording, so nothing is ever
    // overwritten: a re-recording is saved beside the flow as <Name>.recorded.txt, then <Name>.recorded-2.txt and so
    // on, to merge by hand, each with its values beside it. Not as .cs, which would declare the flow's class twice and
    // break the project's build.
    internal static SavedFlow Save(RecordRequest request, GeneratedFlow flow)
    {
        Directory.CreateDirectory(request.OutputDir);
        var flowPath = Path.GetFullPath(Path.Combine(request.OutputDir, $"{request.ClassName}.cs"));
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
