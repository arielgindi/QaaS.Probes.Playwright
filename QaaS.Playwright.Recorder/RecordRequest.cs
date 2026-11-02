using System.Text;
using System.Text.RegularExpressions;

namespace QaaS.Playwright.Recorder;

/// <summary>What to record: the flow's class name, the page to start on, and the folder to save the flow in.</summary>
internal sealed partial record RecordRequest(string ClassName, string Url, string OutputDir)
{
    private const string DefaultOutputDir = "Flows";

    // A file name has at most 255 bytes, and a re-recording adds ".recorded-<n>.txt" to the flow's name.
    private const int MaxNameBytes = 200;

    // Types a flow's code uses, which a flow of the same name would hide.
    private static readonly string[] TypesAFlowUses =
        ["Task", "IPage", "BasePlaywrightFlow", "Expect", "AriaRole", "Regex", "IDialog", "Console", "Array"];

    /// <summary>Parses <c>record &lt;name&gt; &lt;url&gt; [--output-dir Dir]</c>, with the option anywhere.</summary>
    /// <exception cref="ArgumentException">The arguments are not a valid record command.</exception>
    public static RecordRequest Parse(IReadOnlyList<string> args)
    {
        if (args is not ["record", ..]) throw new ArgumentException($"Unknown command '{string.Join(' ', args)}'.");

        var outputDir = DefaultOutputDir;
        var positionals = new List<string>();
        for (var index = 1; index < args.Count; index++)
        {
            if (args[index] == "--output-dir")
                outputDir = index + 1 < args.Count && args[index + 1] is var dir && dir.Trim().Length > 0
                            && !dir.StartsWith('-')
                    ? args[++index]
                    : throw new ArgumentException("--output-dir needs a directory.");
            else if (args[index].StartsWith('-'))
                throw new ArgumentException($"Unknown option '{args[index]}'.");
            else
                positionals.Add(args[index]);
        }

        return positionals is [var name, var url]
            ? Create(name, url, outputDir)
            : throw new ArgumentException("Expected: record <name> <url> [--output-dir Dir]");
    }

    /// <summary>Asks for the page, the flow's name and the folder.</summary>
    public static RecordRequest Ask()
    {
        ConsoleUi.Info("Let's record a browser flow.\n");
        var url = ConsoleUi.Ask("What website do you want to record?", "");
        EnsureHttpUrl(url);
        var name = ConsoleUi.Ask("Give this flow a name", "my-flow");
        return Create(name, url, ConsoleUi.Ask("Where to save it?", DefaultOutputDir));
    }

    // Checked before recording, so nobody records a flow that cannot be saved or compiled.
    private static RecordRequest Create(string name, string url, string outputDir)
    {
        EnsureHttpUrl(url);
        var className = FlowCodeGenerator.ToPascalCase(name);
        var clash = TypesAFlowUses.Contains(className) ? "a type its code uses"
            : WindowsDeviceName().IsMatch(className) ? "a device on Windows"
            : null;
        if (clash is not null)
            throw new ArgumentException($"A flow cannot be named {className}, {clash}. Try {className}Flow.");
        if (Encoding.UTF8.GetByteCount(className) > MaxNameBytes)
            throw new ArgumentException($"The flow's name is too long to be a file name: '{className}'.");
        return new RecordRequest(className, url, outputDir);
    }

    [GeneratedRegex("^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])$", RegexOptions.IgnoreCase)]
    private static partial Regex WindowsDeviceName();

    private static void EnsureHttpUrl(string url)
    {
        var isHttp = Uri.TryCreate(url, UriKind.Absolute, out var uri)
                     && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        if (!isHttp)
            throw new ArgumentException($"'{url}' is not an http(s) URL. Example: https://my-app.com/login");
    }
}
