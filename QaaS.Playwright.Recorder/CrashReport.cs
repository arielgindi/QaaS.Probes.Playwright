using System.Runtime.InteropServices;
using QaaS.Playwright.Browser;
using QaaS.Playwright.Files;

namespace QaaS.Playwright.Recorder;

/// <summary>
/// What the recorder does when it ends as nobody asked it to: on an error nothing caught, or on a signal from outside
/// (SIGTERM, SIGHUP, SIGQUIT, as a <c>kill</c> or a closed terminal sends). It writes the recording as it is then, which
/// autosave keeps for the next start to offer, says so in the console, and writes what happened, with the stack, to
/// <c>~/.qaas/recorder-error.log</c>. Only SIGKILL ends it with nothing said, as no program sees that one.
/// </summary>
internal static class CrashReport
{
    private static readonly List<PosixSignalRegistration> Signals = [];
    private static int _ended;

    /// <summary>Where what ended the recorder is written, newest last.</summary>
    public static string LogPath => Path.Combine(Path.GetDirectoryName(BrowserDefaults.AuthStatePath)!, "recorder-error.log");

    /// <summary>Says and logs every unexpected end from now on.</summary>
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, unhandled) =>
            End(StopReason.Crashed, unhandled.ExceptionObject as Exception, $"stopped on an error: {Describe(unhandled.ExceptionObject as Exception)}", exit: false);
        // A task's error nobody awaited is logged, never the end of a recording.
        TaskScheduler.UnobservedTaskException += (_, unobserved) =>
        {
            Log("An error of a task nobody waited for", unobserved.Exception);
            unobserved.SetObserved();
        };
        foreach (var (signal, reason) in new[] { (PosixSignal.SIGTERM, StopReason.Terminated), (PosixSignal.SIGHUP, StopReason.HungUp), (PosixSignal.SIGQUIT, StopReason.Quit) })
        {
            try
            {
                Signals.Add(PosixSignalRegistration.Create(signal, context =>
                {
                    context.Cancel = true;
                    End(reason, null, $"was stopped from outside ({context.Signal})");
                }));
            }
            catch (PlatformNotSupportedException)
            {
                // This platform sends no such signal.
            }
        }
    }

    /// <summary>
    /// The recording stopped on an error the recorder caught: says so, that the recording is kept, and logs the stack.
    /// </summary>
    public static void Report(Exception failure) =>
        Say(failure, $"stopped on an error: {Describe(failure)}", kept: RecorderLifetime.Current is not null);

    /// <summary>
    /// Ctrl+C before the recording listens for its end (logging in, replaying, a question in the console): the recording
    /// kept as it is, said, and the end with the shell's code for Ctrl+C.
    /// </summary>
    public static void Stop(RecorderLifetime lifetime, StopReason reason) =>
        End(reason, null, reason == StopReason.Interrupted ? "was stopped with Ctrl+C" : $"was stopped ({reason})", lifetime: lifetime);

    // The recording kept as it is by the lifetime of the recording going on, said and logged (a Ctrl+C said only); then,
    // unless the runtime ends the process itself, the end with the shell's code for it.
    private static void End(StopReason reason, Exception? failure, string what, bool exit = true, RecorderLifetime? lifetime = null)
    {
        if (Interlocked.Exchange(ref _ended, 1) == 1) return;
        lifetime ??= RecorderLifetime.Current;
        var kept = lifetime?.Emergency(reason, failure) ?? false;
        Say(failure, what, kept, logIt: reason != StopReason.Interrupted, recording: lifetime is not null);
        if (exit) Environment.Exit(RecorderLifetime.PlanFor(reason).ExitCode);
    }

    private static void Say(Exception? failure, string what, bool kept, bool logIt = true, bool recording = true)
    {
        var logged = logIt && Log($"The recorder {what}", failure);
        var keptText = kept ? " The recording is kept: run qaas-playwright again to save it, continue it or discard it."
            : recording ? " Nothing was recorded yet." : "";
        try
        {
            ConsoleUi.Error($"The recorder {what}.{keptText}" + (logged
                ? $"\n  What happened, with its stack, is in {LogPath}: if it happens again, report it with that file."
                : ""));
        }
        catch (Exception unsaid) when (unsaid is IOException or ObjectDisposedException)
        {
            // The console is gone too.
        }
    }

    /// <summary>Appends what happened, when, and its stack, to <see cref="LogPath"/>; false when it cannot be written.</summary>
    public static bool Log(string what, Exception? failure)
    {
        try
        {
            FileStore.Machine.Append(LogPath, $"{DateTimeOffset.Now:O}  {what}\n{failure?.ToString() ?? Environment.StackTrace}\n\n");
            return true;
        }
        catch (Exception failed) when (failed is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string Describe(Exception? failure) =>
        failure is null ? "unknown" : $"{failure.GetType().Name}: {failure.Message.Split('\n')[0]}";
}
