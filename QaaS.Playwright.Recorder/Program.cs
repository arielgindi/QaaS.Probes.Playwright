using QaaS.Playwright.Recorder.Login;

namespace QaaS.Playwright.Recorder;

/// <summary>
/// Records a browser flow in the installed Chrome and saves it as a C# flow class with its settings.
/// <c>qaas-playwright</c> asks for the details; <c>qaas-playwright record &lt;name&gt; &lt;url&gt; [--output-dir Dir]
/// [--no-panel] [--login Flow --login-assembly Tests.dll]</c> takes them; <c>qaas-playwright --continue &lt;flow&gt;</c>
/// goes on from a flow saved. A recording that did not end in a save is offered first.
/// </summary>
public static class Program
{
    // What a shell returns for a program stopped by Ctrl+C.
    private const int Cancelled = 130;

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
            ConsoleUi.Usage();
            return 0;
        }

        ConsoleUi.CancelOnCtrlC();
        RecordRequest request;
        // The login flow last used in this folder is used again, unless the command says otherwise.
        var projectFolder = Environment.CurrentDirectory;
        try
        {
            if (FlowRecorder.OfferNotSaved() is { } notSaved) return FlowRecorder.Record(notSaved);
            var remembered = RememberedLogin.In(projectFolder);
            request = args is [] or ["record"] ? RecordRequest.Ask(remembered) : RecordRequest.Parse(args, remembered);
        }
        catch (ArgumentException invalid)
        {
            ConsoleUi.Error(ConsoleUi.Reason(invalid));
            ConsoleUi.UsageHint();
            return 1;
        }
        catch (OperationCanceledException)
        {
            ConsoleUi.Cancelled();
            return Cancelled;
        }

        if (RememberedLogin.Remember(projectFolder, request.Login) is { } notRemembered) ConsoleUi.Warning(notRemembered);
        return FlowRecorder.Record(request);
    }
}
