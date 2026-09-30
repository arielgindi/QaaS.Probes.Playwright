using QaaS.Playwright.Browser;

namespace QaaS.Playwright.Recorder;

/// <summary>Records a flow with Playwright codegen and saves it as a C# flow class.</summary>
internal static class FlowRecorder
{
    /// <summary>Returns the process exit code: 0 when a flow was saved.</summary>
    public static int Record(RecordRequest request)
    {
        var codegenOutput = Path.Combine(Path.GetTempPath(), $"qaas-pw-{Guid.NewGuid():N}.codegen.txt");
        try
        {
            ConsoleUi.Recording(request);
            var exitCode = Microsoft.Playwright.Program.Main(CodegenArguments(request.Url, codegenOutput));
            if (exitCode != 0 || !File.Exists(codegenOutput))
            {
                ConsoleUi.Error("Recording cancelled or browser closed before saving.");
                return 1;
            }

            var actions = FlowCodeGenerator.ExtractActions(File.ReadAllText(codegenOutput));
            if (actions.Count == 0)
            {
                ConsoleUi.Error("No actions recorded. Interact with the page before closing.");
                return 1;
            }

            ConsoleUi.Saved(request, Save(request, actions), actions.Count);
            return 0;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            ConsoleUi.Error($"Could not save the recorded flow: {failure.Message}");
            return 1;
        }
        finally
        {
            File.Delete(codegenOutput);
        }
    }

    private static string[] CodegenArguments(string url, string outputPath)
    {
        // The recorder keeps its login between recordings: the first starts logged out, later ones load it.
        var authState = BrowserDefaults.AuthStatePath;
        Directory.CreateDirectory(Path.GetDirectoryName(authState)!);
        string[] loadAuthState = File.Exists(authState) ? ["--load-storage", authState] : [];
        return
        [
            "codegen", "--target", "csharp-nunit", "--output", outputPath,
            "--channel", BrowserDefaults.ChromeChannel, "--viewport-size", BrowserDefaults.RecorderViewport,
            // Codegen ignores id attributes; record the test id the probe resolves GetByTestId() against instead.
            "--test-id-attribute", BrowserDefaults.TestIdAttribute,
            "--save-storage", authState, .. loadAuthState,
            url,
        ];
    }

    // A flow may have been edited by hand since it was recorded, so a re-recording never overwrites it: it is saved
    // beside it as <Name>.recorded.cs, to merge by hand.
    private static string Save(RecordRequest request, IReadOnlyList<string> actions)
    {
        Directory.CreateDirectory(request.OutputDir);
        var path = Path.GetFullPath(Path.Combine(request.OutputDir, $"{request.ClassName}.cs"));
        if (File.Exists(path)) path = Path.ChangeExtension(path, ".recorded.cs");

        var source = FlowCodeGenerator.Render(request.ClassName, actions, ProjectNamespace.Of(request.OutputDir));
        File.WriteAllText(path, source);
        return path;
    }
}
