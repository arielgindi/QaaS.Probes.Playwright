using System.Runtime.InteropServices;
using QaaS.Playwright.Browser;

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
    private static Func<Task<bool>>? _keep;
    private static int _ended;

    /// <summary>Where what ended the recorder is written, newest last.</summary>
    public static string LogPath => Path.Combine(Path.GetDirectoryName(BrowserDefaults.AuthStatePath)!, "recorder-error.log");

    /// <summary>Says and logs every unexpected end from now on.</summary>
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, unhandled) =>
            End(unhandled.ExceptionObject as Exception, $"stopped on an error: {Describe(unhandled.ExceptionObject as Exception)}", exit: null);
        // A task's error nobody awaited is logged, never the end of a recording.
        TaskScheduler.UnobservedTaskException += (_, unobserved) =>
        {
            Log("An error of a task nobody waited for", unobserved.Exception);
            unobserved.SetObserved();
        };
        foreach (var (signal, code) in new[] { (PosixSignal.SIGTERM, 143), (PosixSignal.SIGHUP, 129), (PosixSignal.SIGQUIT, 131) })
        {
            try
            {
                Signals.Add(PosixSignalRegistration.Create(signal, context =>
                {
                    context.Cancel = true;
                    End(null, $"was stopped from outside ({context.Signal})", code);
                }));
            }
            catch (PlatformNotSupportedException)
            {
                // This platform sends no such signal.
            }
        }
    }

    /// <summary>
    /// While recording, how to write the recording as it is, for an unexpected end to keep it, returning whether there was
    /// anything to keep; disposing the result forgets it.
    /// </summary>
    public static IDisposable Keeping(Func<Task<bool>> keep)
    {
        _keep = keep;
        return new Forget();
    }

    /// <summary>
    /// The recording stopped on an error the recorder caught: says so, that the recording is kept, and logs the stack.
    /// </summary>
    public static void Report(Exception failure) =>
        Say(failure, $"stopped on an error: {Describe(failure)}", kept: _keep is not null);

    /// <summary>
    /// Ctrl+C while a recording goes on, but does not end on it (it is ending, or has not begun): keeps the recording as
    /// it is, says so, and ends with the shell's code for Ctrl+C. False when no recording goes on.
    /// </summary>
    public static bool Interrupted()
    {
        if (_keep is null) return false;
        End(null, "was stopped with Ctrl+C", 130, logged: false);
        return true;
    }

    // The recording kept as it is, said and logged (a Ctrl+C, said only); then, for a signal, the end with the shell's
    // code for it.
    private static void End(Exception? failure, string what, int? exit, bool logged = true)
    {
        if (Interlocked.Exchange(ref _ended, 1) == 1) return;
        var kept = false;
        try
        {
            if (_keep is { } keep && keep() is var keeping && keeping.Wait(TimeSpan.FromSeconds(3))) kept = keeping.Result;
        }
        catch (Exception notKept) when (notKept is not OutOfMemoryException)
        {
            Log("Could not keep the recording", notKept);
        }

        Say(failure, what, kept, logged);
        if (exit is { } code) Environment.Exit(code);
    }

    private static void Say(Exception? failure, string what, bool kept, bool logIt = true)
    {
        var logged = logIt && Log($"The recorder {what}", failure);
        var keptText = kept ? " The recording is kept: run qaas-playwright again to save it, continue it or discard it."
            : _keep is not null ? " Nothing was recorded yet." : "";
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
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O}  {what}\n{failure?.ToString() ?? Environment.StackTrace}\n\n");
            return true;
        }
        catch (Exception failed) when (failed is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string Describe(Exception? failure) =>
        failure is null ? "unknown" : $"{failure.GetType().Name}: {failure.Message.Split('\n')[0]}";

    private sealed class Forget : IDisposable
    {
        public void Dispose() => _keep = null;
    }
}
