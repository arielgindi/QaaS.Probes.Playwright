namespace QaaS.Playwright.Recorder;

/// <summary>What to record: the flow's class name, the page to start on, and the folder to save the flow in.</summary>
internal sealed record RecordRequest(string ClassName, string Url, string OutputDir)
{
    private const string DefaultOutputDir = "Flows";

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
                outputDir = index + 1 < args.Count && !args[index + 1].StartsWith('-')
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

    private static RecordRequest Create(string name, string url, string outputDir)
    {
        EnsureHttpUrl(url);
        return new RecordRequest(FlowCodeGenerator.ToPascalCase(name), url, outputDir);
    }

    private static void EnsureHttpUrl(string url)
    {
        var isHttp = Uri.TryCreate(url, UriKind.Absolute, out var uri)
                     && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        if (!isHttp)
            throw new ArgumentException($"'{url}' is not an http(s) URL. Example: https://my-app.com/login");
    }
}
