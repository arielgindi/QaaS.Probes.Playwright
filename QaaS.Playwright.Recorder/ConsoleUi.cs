namespace QaaS.Playwright.Recorder;

/// <summary>Everything the recorder prints and asks.</summary>
internal static class ConsoleUi
{
    public static void Header()
    {
        Console.WriteLine();
        WithColor(ConsoleColor.Magenta, () => Console.WriteLine(
            "  +-----------------------------------+\n" +
            "  |   QaaS Playwright Recorder        |\n" +
            "  +-----------------------------------+\n"));
    }

    public static void Usage() => Console.WriteLine("""
          Usage:
            dotnet run                                          Interactive mode
            dotnet run -- record <name> <url>                   Quick record
            dotnet run -- record <name> <url> --output-dir Dir  Record to folder

          Uses your system-installed Google Chrome.

        """);

    public static string Ask(string prompt, string defaultValue)
    {
        WithColor(ConsoleColor.White, () => Console.Write($"  {prompt}"));
        if (defaultValue.Length > 0) WithColor(ConsoleColor.DarkGray, () => Console.Write($" [{defaultValue}]"));
        Console.Write(": ");
        var answer = Console.ReadLine()?.Trim() ?? "";
        return answer.Length > 0 ? answer : defaultValue;
    }

    public static void Recording(RecordRequest request)
    {
        Separator();
        Info($"Flow:    {request.ClassName}");
        Info($"URL:     {request.Url}");
        Info($"Save to: {Path.GetFullPath(Path.Combine(request.OutputDir, $"{request.ClassName}.cs"))}\n");
        Info(">>> Browser is opening — do your thing, then CLOSE the browser when done.\n");
    }

    public static void Saved(RecordRequest request, string path, int actionCount)
    {
        Separator();
        WithColor(ConsoleColor.Green, () => Info($"SAVED {path}"));
        Info($"{actionCount} actions recorded");
        if (!path.EndsWith(".cs", StringComparison.Ordinal))
            Info("(the existing flow was kept — merge the new actions into it by hand)");

        Console.WriteLine();
        Info("Next steps:\n");
        Info("1. Add to your test.qaas.yaml:");
        Hint($"""
            Sessions:
              - Name: MySession
                Probes:
                  - Name: {request.ClassName}
                    Probe: PlaywrightFlowProbe
                    ProbeConfiguration:
                      BaseUrl: {request.Url}
                      Flows: [{request.ClassName}]
            """);
        Console.WriteLine();
        Info("2. Run it:");
        Hint("dotnet run -- run test.qaas.yaml");
        Console.WriteLine();
    }

    public static void Info(string message) => Console.WriteLine($"  {message}");

    public static void Error(string message) => WithColor(ConsoleColor.Red, () => Console.WriteLine($"\n  {message}\n"));

    private static void Hint(string lines)
    {
        var indented = string.Join('\n', lines.Split('\n').Select(line => $"     {line}"));
        WithColor(ConsoleColor.DarkGray, () => Console.WriteLine(indented));
    }

    private static void Separator() =>
        WithColor(ConsoleColor.DarkGray, () => Console.WriteLine("  ---------------------------------------"));

    private static void WithColor(ConsoleColor color, Action write)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        try { write(); }
        finally { Console.ForegroundColor = previous; }
    }
}
