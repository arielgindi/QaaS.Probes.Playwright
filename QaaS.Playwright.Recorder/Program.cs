using System.Reflection;

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
        switch (args)
        {
            case ["--version"]:
                Console.WriteLine(Version);
                return 0;
            case ["--help"] or ["-h"] or ["help"]:
                ConsoleUi.Header();
                ConsoleUi.Usage();
                return 0;
        }

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

    private static string Version =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? "unknown";
}
