using System.Text;
using System.Text.RegularExpressions;

namespace QaaS.Playwright.Recorder;

/// <summary>What to record: the flow's class name, the page to start on, and the folder to save the flow in.</summary>
internal sealed partial record RecordRequest(string ClassName, string Url, string OutputDir)
{
    /// <summary>Save the raw recording beside the flow, with every locator tried for each element.</summary>
    public bool KeepRecording { get; init; }

    private const string DefaultOutputDir = "Flows";

    // A file name has at most 255 bytes, and a re-recording adds ".recorded-<n>.txt" to the flow's name.
    private const int MaxNameBytes = 200;

    // Types a flow's code uses, which a flow of the same name would hide.
    private static readonly string[] TypesAFlowUses =
        ["Task", "IPage", "BasePlaywrightFlow", "Expect", "AriaRole", "Regex", "IDialog", "Console", "Array"];

    /// <summary>Whether the recorder's panel shows the flow beside the recorded window as it is written.</summary>
    public bool Panel { get; init; } = true;

    /// <summary>
    /// Parses <c>record &lt;name&gt; &lt;url&gt; [--output-dir Dir] [--keep-recording] [--no-panel]</c>, with the options anywhere.
    /// </summary>
    /// <exception cref="ArgumentException">The arguments are not a valid record command.</exception>
    public static RecordRequest Parse(IReadOnlyList<string> args)
    {
        if (args is not ["record", ..]) throw new ArgumentException($"Unknown command '{string.Join(' ', args)}'.");

        var outputDir = DefaultOutputDir;
        var keepRecording = false;
        var panel = true;
        var positionals = new List<string>();
        for (var index = 1; index < args.Count; index++)
        {
            if (args[index] == "--output-dir")
                outputDir = index + 1 < args.Count && args[index + 1] is var dir && dir.Trim().Length > 0
                            && !dir.StartsWith('-')
                    ? args[++index]
                    : throw new ArgumentException("--output-dir needs a directory.");
            else if (args[index] == "--keep-recording")
                keepRecording = true;
            else if (args[index] == "--no-panel")
                panel = false;
            else if (args[index].StartsWith('-'))
                throw new ArgumentException($"Unknown option '{args[index]}'.");
            else
                positionals.Add(args[index]);
        }

        return positionals is [var name, var url]
            ? Create(name, url, outputDir) with { KeepRecording = keepRecording, Panel = panel }
            : throw new ArgumentException("Expected: record <name> <url> [--output-dir Dir] [--keep-recording] [--no-panel]");
    }

    /// <summary>Asks for the page, the flow's name and the folder, again until each answer is valid.</summary>
    /// <exception cref="OperationCanceledException">The input ended before all were given.</exception>
    public static RecordRequest Ask()
    {
        ConsoleUi.Intro("Let's record a browser flow.");
        var url = ConsoleUi.Ask("URL to start on", "", answer => answer.Length == 0
            ? "Type the address of the page to start on, e.g. https://my-app.com/login"
            : Refusal(() => EnsureHttpUrl(answer)));
        var name = ConsoleUi.Ask("Flow name", "my-flow", answer => Refusal(() => Create(answer, url, DefaultOutputDir)));
        return Create(name, url, ConsoleUi.Ask("Save in folder", DefaultOutputDir));
    }

    private static string? Refusal(Action check)
    {
        try
        {
            check();
            return null;
        }
        catch (ArgumentException refused)
        {
            return ConsoleUi.Reason(refused);
        }
    }

    /// <summary>The same request for a flow named <paramref name="name"/>, checked as a new name is.</summary>
    /// <exception cref="ArgumentException">The flow cannot have that name.</exception>
    public RecordRequest Renamed(string name) => Create(name, Url, OutputDir) with { Panel = Panel };

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
