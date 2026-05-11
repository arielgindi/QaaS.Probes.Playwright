namespace QaaS.Probes.Playwright.Recorder;

internal static class ConsoleUi
{
    public static void Header()
    {
        Console.WriteLine();
        WithColor(ConsoleColor.Magenta, () =>
        {
            Console.WriteLine("  +-----------------------------------+");
            Console.WriteLine("  |   QaaS Playwright Recorder        |");
            Console.WriteLine("  +-----------------------------------+");
        });
        Console.WriteLine();
    }

    public static void Separator() =>
        WithColor(ConsoleColor.DarkGray, () => Console.WriteLine("  ---------------------------------------"));

    public static void Info(string msg) =>
        Console.WriteLine($"  {msg}");

    public static void Error(string msg) =>
        WithColor(ConsoleColor.Red, () => Console.WriteLine($"\n  {msg}\n"));

    public static void Success(string msg) =>
        WithColor(ConsoleColor.Green, () => Console.WriteLine($"  {msg}"));

    public static void Hint(string msg) =>
        WithColor(ConsoleColor.DarkGray, () => Console.WriteLine($"     {msg}"));

    public static string Ask(string prompt, string defaultValue)
    {
        WithColor(ConsoleColor.White, () => Console.Write($"  {prompt}"));
        if (!string.IsNullOrEmpty(defaultValue))
            WithColor(ConsoleColor.DarkGray, () => Console.Write($" [{defaultValue}]"));
        Console.Write(": ");
        var input = Console.ReadLine()?.Trim() ?? "";
        return string.IsNullOrEmpty(input) ? defaultValue : input;
    }

    private static void WithColor(ConsoleColor color, Action act)
    {
        var prev = Console.ForegroundColor;
        try { Console.ForegroundColor = color; act(); }
        finally { Console.ForegroundColor = prev; }
    }
}
