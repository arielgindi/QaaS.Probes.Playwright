using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Generation;

namespace QaaS.Playwright.Recorder;

/// <summary>Everything the recorder prints and asks.</summary>
internal static class ConsoleUi
{
    private static readonly Lock Gate = new();

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
            qaas-playwright                                       Asks for the URL, the name and the folder
            qaas-playwright record <name> <url>                   Records into ./Flows
            qaas-playwright record <name> <url> --output-dir Dir  Records into Dir
            qaas-playwright record <name> <url> --keep-recording  Also saves the raw recording beside the flow
            qaas-playwright record <name> <url> --no-panel        Records without the panel beside the browser
            qaas-playwright --version                             Prints the version

          Uses your installed Google Chrome.

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
        Info(">>> Browser is opening — do your thing, then CLOSE the browser (or press Enter here) when done.");
        Info("    The box under the pointer shows what a click records. F9 hides or shows it;");
        Info("    Alt+↑ picks the element around it and Alt+↓ goes back, for the next click to record.\n");
    }

    /// <summary>Completes when the person presses Enter; never, when nobody can type here.</summary>
    public static Task EnterPressedAsync() =>
        Console.IsInputRedirected ? new TaskCompletionSource().Task : Task.Run(() => Console.ReadLine());

    /// <summary>Shows an action as it is captured, and what the page did after it.</summary>
    public static void Captured(object news)
    {
        var line = news switch
        {
            CapturedAction action => Describe(action),
            CapturedEffect effect => Describe(effect),
            _ => null,
        };
        if (line is null) return;
        lock (Gate)
        {
            if (news is CapturedAction) WithColor(ConsoleColor.Green, () => Console.Write("  ✓ "));
            else Console.Write("      ");
            Console.WriteLine(line);
        }
    }

    public static void Saved(RecordRequest request, SavedFlow saved, GeneratedFlow flow)
    {
        Separator();
        WithColor(ConsoleColor.Green, () => Info($"SAVED {saved.CodePath}"));
        Info($"      {saved.YamlPath} (the values it recorded)");
        if (saved.RecordingPath is { } raw) Info($"      {raw} (the raw recording, with every locator tried)");
        if (!saved.CodePath.EndsWith(".cs", StringComparison.Ordinal))
            Info("(the existing flow was kept — merge the new recording into it by hand)");

        if (flow.Checks.Count > 0)
        {
            Console.WriteLine();
            WithColor(ConsoleColor.Yellow, () => Info("Check by hand:"));
            foreach (var check in flow.Checks)
                WithColor(ConsoleColor.Yellow, () => Info(check.Line is { } line ? $"  line {line}: {check.Message}" : $"  {check.Message}"));
        }

        Console.WriteLine();
        Info("Next steps:\n");
        Info("1. Add to your test.qaas.yaml:");
        var values = string.Join('\n', flow.Yaml.Split('\n').Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => "          " + line));
        Hint($"""
            Sessions:
              - Name: MySession
                Probes:
                  - Name: {request.ClassName}
                    Probe: PlaywrightFlowProbe
                    ProbeConfiguration:
                      BaseUrl: '{request.Url.Replace("'", "''")}'
                      Flows: [{request.ClassName}]
            {values}
            """);
        Console.WriteLine();
        Info("2. Run it:");
        Hint("dotnet run -- run test.qaas.yaml");
        Console.WriteLine();
    }

    public static void Info(string message) => Console.WriteLine($"  {message}");

    public static void Warning(string message) => WithColor(ConsoleColor.Yellow, () => Console.WriteLine($"\n  {message}\n"));

    public static void Error(string message) => WithColor(ConsoleColor.Red, () => Console.WriteLine($"\n  {message}\n"));

    private static string? Describe(CapturedAction action)
    {
        var target = action.Kind == "choose" ? action.Field ?? action.Target : action.Target;
        var where = LocatorPolicy.Describe(LocatorPolicy.Choose(target, byName: action.Widget == "radio")) ?? target?.Text ?? "";
        return action.Kind switch
        {
            "click" when action.Widget is "select-open" or "autocomplete-button" => null,
            "fill" => $"fill {where} = {action.Value}",
            "choose" => $"choose {where} → {action.Value}",
            "press" => $"press {action.Key}{(action.OnPage == true ? "" : $" in {where}")}",
            "upload" => $"upload {where} ← {string.Join(", ", action.Files ?? [])}",
            _ => $"{action.Kind} {where}",
        };
    }

    private static string? Describe(CapturedEffect effect) => effect.Kind switch
    {
        "dialogOpened" => $"↳ dialog opened: {effect.Dialog?.Title}",
        "dialogClosed" => $"↳ dialog closed: {effect.Dialog?.Title}",
        "alert" => $"↳ alert: {effect.Text}",
        "tabSelected" => "↳ tab selected",
        _ => null,
    };

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

    // ------------------------------------------------------------------------- the login flow, pausing, F4 (appended)

    public static void LoginUsage() => Console.WriteLine("""
          Log in first, with a flow of your tests, so the recording starts logged in:
            qaas-playwright record <name> <url> --login LoginFlow --login-assembly bin/Debug/net10.0/MyTests.dll
                [--login-settings Flows/LoginFlow.flow.yaml]
          The login is used again in this folder until --no-login.

        """);

    /// <summary>The keys of the recorded window besides those <see cref="Recording"/> tells.</summary>
    public static void RecorderKeys() =>
        Info("    F8 pauses or resumes the recording; F4 freezes the page's elements for the panel's Elements tab.\n");

    public static void LoggingIn(Login.LoginFlow login, bool remembered)
    {
        Info($"Logging in with {login.Flow} from {Path.GetFileName(login.Assembly)}…");
        if (remembered) Hint("as last time in this folder; --no-login records without it");
    }

    public static void LoggedIn(string flow, string url) =>
        WithColor(ConsoleColor.Green, () => Info($"✓ Logged in with {flow}. Recording from {url}\n"));

    public static void LoginFailed(string flow, string reason)
    {
        Error($"{flow} failed: {reason}");
        Info("Nothing is recorded yet. Log in by hand in the browser, then press F8 there (or Resume in the panel) to");
        Info("start recording. To quit, close the browser.\n");
    }

    /// <summary>A line the login flow logged.</summary>
    public static void FlowLog(string message) => WithColor(ConsoleColor.DarkGray, () => Info($"  {message}"));

    public static void StateChanged(RecordingState state)
    {
        lock (Gate)
        {
            if (state == RecordingState.Paused)
                WithColor(ConsoleColor.Yellow, () => Info("❚❚ Paused: nothing is recorded. F8 or Resume in the panel goes on."));
            else if (state == RecordingState.Recording)
                WithColor(ConsoleColor.Green, () => Info("▶ Recording, from the page the app is on now."));
        }
    }

    public static void Froze(FrozenElements frozen)
    {
        lock (Gate) WithColor(ConsoleColor.DarkGray, () => Info($"  ❄ The elements of {frozen.Page} are frozen: pick them in the panel's Elements tab."));
    }

    /// <summary>After <see cref="Saved"/>, for a flow recorded after a login flow.</summary>
    public static void RunAfter(string loginFlow)
    {
        Info($"It was recorded after {loginFlow}: run that first, with SetupFlows: [{loginFlow}] and its settings.");
        Console.WriteLine();
    }
}
