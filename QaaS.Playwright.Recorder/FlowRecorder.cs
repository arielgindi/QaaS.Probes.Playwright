using System.Diagnostics;
using Microsoft.Playwright;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Generation;

namespace QaaS.Playwright.Recorder;

/// <summary>The flow's two saved files: its class and its recorded values.</summary>
internal sealed record SavedFlow(string CodePath, string YamlPath);

/// <summary>Records a flow in Chrome and saves it as a C# flow class with the YAML of its values.</summary>
internal static class FlowRecorder
{
    /// <summary>Returns the process exit code: 0 when a flow was saved.</summary>
    public static int Record(RecordRequest request) => RecordAsync(request).GetAwaiter().GetResult();

    private static async Task<int> RecordAsync(RecordRequest request)
    {
        ConsoleUi.Recording(request);
        Recording recording;
        try
        {
            var session = await RecordingSession.StartAsync(new RecordingOptions(request.Url), ConsoleUi.Captured);
            await Task.WhenAny(session.Closed, ConsoleUi.EnterPressedAsync());
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
        try
        {
            ConsoleUi.Saved(request, Save(request, flow), flow);
            return 0;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Recording it again would take as long as the first time.
            var kept = Path.Combine(Path.GetTempPath(), $"{request.ClassName}-{Guid.NewGuid():N}.cs.txt");
            await File.WriteAllTextAsync(kept, flow.Code + "\n/*\n" + flow.Yaml + "*/\n");
            ConsoleUi.Error($"Could not save the flow: {failure.Message}\n  The recorded flow is kept in {kept}");
            return 1;
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
