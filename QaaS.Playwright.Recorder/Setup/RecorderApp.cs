using Microsoft.Playwright;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Panel;

namespace QaaS.Playwright.Recorder.Setup;

/// <summary>How the recorder runs: where it was started, and, for its tests, Chrome's flags and the test runs' options.</summary>
internal sealed record RecorderHostOptions
{
    public string StartFolder { get; init; } = Folders.Here;

    /// <summary>Chrome's flags: none for a person; <c>--headless=new</c> for the recorder's tests.</summary>
    public IReadOnlyList<string> BrowserArgs { get; init; } = [];

    /// <summary>Whether recordings start from, and keep, the login the recorder saved; its tests keep none.</summary>
    public bool KeepsLogin { get; init; } = true;
}

/// <summary>The recorder running: its Chrome, its window and the start screen in it, for its tests to drive.</summary>
internal sealed record RecorderHost(RecorderBrowser Browser, PanelHost Window, SetupScreen Setup) : IAsyncDisposable
{
    public bool KeepsLogin { get; init; } = true;

    /// <summary>The recorder's Chrome and window, as each recording is given them.</summary>
    public RecorderWindow Recording => new(Browser, Window) { KeepsLogin = KeepsLogin };

    public async ValueTask DisposeAsync()
    {
        await Window.DisposeAsync();
        await Browser.DisposeAsync();
    }
}

/// <summary>
/// The recorder: Chrome opens on its window, the start screen, and each recording set up there runs in it, beside the
/// app's window, until the window is closed. The console only says what goes on.
/// </summary>
internal static class RecorderApp
{
    private const int Width = 1040;
    private const int Height = 760;

    /// <summary>Main's body: the window, recording after recording, until it closes. Returns the exit code.</summary>
    public static async Task<int> RunAsync(RecorderHostOptions options)
    {
        RecorderHost host;
        try
        {
            host = await OpenAsync(options);
        }
        catch (PlaywrightException failure)
        {
            ConsoleUi.Error($"The recorder's window could not open: {failure.Message.Split('\n')[0]}\n  It is a window of Google " +
                            "Chrome: install Chrome, and run the recorder where there is a desktop to show it on.");
            return 1;
        }

        await using (host)
        {
            ConsoleUi.QuitOnCtrlC(host.Setup.Quit);
            var exit = 0;
            while (await host.Setup.NextAsync() is { } request)
            {
                ConsoleUi.QuitOnCtrlC(null);
                // The project is where the recording runs, as if the recorder had been started there.
                if (host.Setup.Project.Folder is { } project && Directory.Exists(project)) Environment.CurrentDirectory = project;
                var outcome = new RecordingOutcome();
                exit = await FlowRecorder.RecordAsync(request, host.Recording, outcome);
                if (host.Window.Closed.IsCompleted) break;
                ConsoleUi.QuitOnCtrlC(host.Setup.Quit);
                await host.Setup.EndedAsync(outcome);
                ConsoleUi.BackAtStart(outcome.Saved);
            }

            return host.Setup.State.Stop is StopReason.CtrlC ? 130 : exit;
        }
    }

    /// <summary>Launches Chrome on the recorder's window, the start screen in it, in the middle of the screen.</summary>
    /// <exception cref="PlaywrightException">Chrome is not installed, or could not open the window.</exception>
    public static async Task<RecorderHost> OpenAsync(RecorderHostOptions options)
    {
        var browser = await RecorderBrowser.LaunchAsync(options.BrowserArgs);
        try
        {
            var window = await PanelHost.OpenAsync(browser.Browser);
            await PanelWindow.CentreAsync(window.Page, Width, Height);
            ConsoleUi.WindowOpen();
            return new RecorderHost(browser, window, new SetupScreen(window, options.StartFolder)) { KeepsLogin = options.KeepsLogin };
        }
        catch
        {
            await browser.DisposeAsync();
            throw;
        }
    }
}
