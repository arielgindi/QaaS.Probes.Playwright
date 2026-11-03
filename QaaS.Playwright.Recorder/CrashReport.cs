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
    private static Func<Task>? _keep;
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
    /// While recording, how to write the recording as it is, for an unexpected end to keep it; disposing the result forgets
    /// it.
    /// </summary>
    public static IDisposable Keeping(Func<Task> keep)
    {
        _keep = keep;
        return new Forget();
    }

    /// <summary>
    /// The recording stopped on an error the recorder caught: says so, that the recording is kept, and logs the stack.
    /// </summary>
    public static void Report(Exception failure) =>
        Say(failure, $"stopped on an error: {Describe(failure)}");

    // The recording kept as it is, said and logged; then, for a signal, the end with the shell's code for it.
    private static void End(Exception? failure, string what, int? exit)
    {
        if (Interlocked.Exchange(ref _ended, 1) == 1) return;
        try
        {
            if (_keep is { } keep) keep().Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception notKept) when (notKept is not OutOfMemoryException)
        {
            Log("Could not keep the recording", notKept);
        }

        Say(failure, what);
        if (exit is { } code) Environment.Exit(code);
    }

    private static void Say(Exception? failure, string what)
    {
        var logged = Log($"The recorder {what}", failure);
        var kept = _keep is not null ? " The recording is kept: run qaas-playwright again to save it, continue it or discard it." : "";
        try
        {
            ConsoleUi.Error($"The recorder {what}.{kept}" + (logged ? $"\n  What happened is in {LogPath}." : ""));
        }
        catch (Exception unsaid) when (unsaid is IOException or ObjectDisposedException)
        {
            // The console is gone too.
        }
    }

    // Appends what happened, when, and its stack; false when the log cannot be written.
    private static bool Log(string what, Exception? failure)
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
