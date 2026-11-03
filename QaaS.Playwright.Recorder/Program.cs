using System.Reflection;
using QaaS.Playwright.Recorder.Login;

namespace QaaS.Playwright.Recorder;

/// <summary>
/// Records a browser flow in the installed Chrome and saves it as a C# flow class with its settings.
/// <c>qaas-playwright</c> asks for the details; <c>qaas-playwright record &lt;name&gt; &lt;url&gt; [--output-dir Dir]
/// [--no-panel] [--login Flow --login-assembly Tests.dll]</c> takes them.
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
                ConsoleUi.LoginUsage();
                return 0;
        }

        ConsoleUi.Header();
        RecordRequest request;
        // The login flow last used in this folder is used again, unless the command says otherwise.
        var projectFolder = Environment.CurrentDirectory;
        try
        {
            var remembered = RememberedLogin.In(projectFolder);
            request = args is [] or ["record"] ? RecordRequest.Ask(remembered) : RecordRequest.Parse(args, remembered);
        }
        catch (ArgumentException invalid)
        {
            ConsoleUi.Error(invalid.Message);
            ConsoleUi.Usage();
            ConsoleUi.LoginUsage();
            return 1;
        }

        if (RememberedLogin.Remember(projectFolder, request.Login) is { } notRemembered) ConsoleUi.Warning(notRemembered);
        return FlowRecorder.Record(request);
    }

    private static string Version =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? "unknown";
}
