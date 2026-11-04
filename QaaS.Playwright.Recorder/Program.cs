using QaaS.Playwright.Recorder.Setup;

namespace QaaS.Playwright.Recorder;

/// <summary>
/// Records a browser flow in the installed Chrome and saves it as a C# flow class with its settings. It takes no
/// options: <c>qaas-playwright</c> opens its window, the start screen, where each recording is set up, and everything
/// happens there. <c>--version</c> prints the version, and <c>--help</c> says where things are.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args is ["--version"] or ["-v"])
        {
            ConsoleUi.PrintVersion();
            return 0;
        }

        ConsoleUi.Header();
        if (args is ["help"] || args.Any(arg => arg is "--help" or "-h"))
        {
            ConsoleUi.Help();
            return 0;
        }

        // Anything else given is said, and left: the window is where a recording is set up.
        if (args.Length > 0) ConsoleUi.NoOptions(args);
        ConsoleUi.CancelOnCtrlC();
        CrashReport.Install();
        return RecorderApp.RunAsync(new RecorderHostOptions()).GetAwaiter().GetResult();
    }
}
