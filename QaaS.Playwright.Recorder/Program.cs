namespace QaaS.Playwright.Recorder;

/// <summary>
/// Records a browser flow in the installed Chrome and saves it as a C# flow class with its settings.
/// <c>qaas-playwright</c> asks for the details; <c>qaas-playwright record &lt;name&gt; &lt;url&gt; [--output-dir Dir]
/// [--no-panel]</c> takes them.
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
            ConsoleUi.Usage();
            return 0;
        }

        RecordRequest request;
        try
        {
            request = args is [] or ["record"] ? RecordRequest.Ask() : RecordRequest.Parse(args);
        }
        catch (ArgumentException invalid)
        {
            ConsoleUi.Error(invalid.Message);
            ConsoleUi.UsageHint();
            return 1;
        }

        return FlowRecorder.Record(request);
    }
}
