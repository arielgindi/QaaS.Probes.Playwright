namespace QaaS.Playwright.Recorder;

/// <summary>
/// Records a browser flow with Playwright codegen in the system Chrome and saves it as a C# flow class. <c>dotnet run</c>
/// asks for the details; <c>dotnet run -- record &lt;name&gt; &lt;url&gt; [--output-dir Dir]</c> takes them.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        ConsoleUi.Header();
        RecordRequest request;
        try
        {
            request = args is [] or ["record"] ? RecordRequest.Ask() : RecordRequest.Parse(args);
        }
        catch (ArgumentException invalid)
        {
            ConsoleUi.Error(invalid.Message);
            ConsoleUi.Usage();
            return 1;
        }

        return FlowRecorder.Record(request);
    }
}
