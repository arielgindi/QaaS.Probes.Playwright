using System.Reflection;
using System.Runtime.InteropServices;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Generation;

namespace QaaS.Playwright.Recorder;

/// <summary>
/// Everything the recorder prints and asks. Each kind of line has a method of its own: a step and what it caused, info,
/// success, a warning, an error, a phase, the recording's state. They all draw a glyph, a tone and a message with
/// <see cref="Status"/>, so a new kind is one more short method. <see cref="Terminal"/> tells colours, glyphs and width.
/// </summary>
internal static class ConsoleUi
{
    private const string Margin = "  ";

    // A step's line: "  12 • click    Save order                     button "Save order"".
    private const int NumberWidth = 3;
    private const int VerbWidth = 8;
    private const int SubjectWidth = 30;
    private const int LeastLocator = 16;
    private static readonly int StepIndent = Margin.Length + NumberWidth + 3;

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, int> StepOfAction = [];
    private static int _lastStep;
    private static bool _recording;
    private static Capture.RecordingState _state;
    private static int _cancelled;

    public static string Version =>
        typeof(ConsoleUi).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? "unknown";

    private static Terminal Out => Terminal.Out;

    // ── The header, the help and the version ────────────────────────────────────────────────────────────────────

    public static void Header()
    {
        var t = Out;
        t.WriteLine();
        var badge = t.Color ? t.Paint(" QaaS ", Tone.Badge) + " " : "QaaS ";
        t.WriteLine(Margin + badge + t.Paint("Playwright Recorder", Tone.Bold) + t.Paint($"  v{Version}", Tone.Dim));
    }

    public static void Usage()
    {
        var t = Out;
        t.WriteLine();
        foreach (var line in Terminal.Wrap(
                     "Records what you do in Chrome and saves it as a QaaS flow class, with the values you typed and " +
                     "chose as its settings.", Math.Min(t.Width - 1, 80) - Margin.Length))
            t.WriteLine(Margin + line);

        (string, string)[] usage =
        [
            ("qaas-playwright", "Asks for the URL, the name and the folder"),
            ($"qaas-playwright record {t.Paint("<name> <url>", Tone.Dim)}", "Records into ./Flows"),
        ];
        (string, string)[] options =
        [
            ($"--output-dir {t.Paint("<dir>", Tone.Dim)}", "Saves the flow in <dir> instead of ./Flows"),
            ("--keep-recording", "Also saves the raw recording, with every locator tried"),
            ("--no-panel", "Records without the panel beside the browser"),
            ($"--login {t.Paint("<flow>", Tone.Dim)}", "Logs in first with a flow of your tests, e.g. LoginFlow"),
            ($"--login-assembly {t.Paint("<dll>", Tone.Dim)}", "The built test project that holds it"),
            ($"--login-settings {t.Paint("<yaml>", Tone.Dim)}", "Its settings, if it has any"),
            ("--no-login", "Records without logging in, and forgets the login of this folder"),
            ("-h, --help", "Shows this help"),
            ("-v, --version", "Prints the version"),
        ];
        (string, string)[] keys =
        [
            ("F9", "Hides or shows the box over the element a click records"),
            ($"Alt+{t.Glyph("↑", "Up")} / Alt+{t.Glyph("↓", "Down")}", "Picks the element around the box, or goes back"),
            ("F8", "Pauses or resumes the recording"),
            ("F4", "Freezes the page's elements for the panel's Elements tab"),
            ("Ctrl+Enter", "In the panel: tests the flow so far, as it will be saved"),
            ("Enter", "Finishes and saves; so do Save in the panel and closing Chrome"),
        ];
        Rows(t, "USAGE", usage, ColumnFor(usage), Tone.Plain);
        Rows(t, "OPTIONS", options, ColumnFor([.. options, .. keys]));
        Rows(t, "WHILE RECORDING", keys, ColumnFor([.. options, .. keys]));

        Section(t, "EXAMPLES");
        foreach (var example in (string[])
                 [
                     "qaas-playwright record login-flow https://my-app.com",
                     "qaas-playwright record login-flow https://my-app.com --output-dir UiFlows",
                     "qaas-playwright record new-ticket https://my-app.com --login LoginFlow --login-assembly bin/Debug/net10.0/MyTests.dll",
                 ])
            t.WriteLine(Margin + Margin + t.Paint("$ ", Tone.Dim) + example);

        t.WriteLine();
        t.WriteLine(Margin + t.Paint("Uses your installed Google Chrome. More in docs/RECORDING.md.", Tone.Dim));
        t.WriteLine();
    }

    /// <summary>The lines under an error in the command line.</summary>
    public static void UsageHint()
    {
        var t = Out;
        t.WriteLine(Margin + t.Paint("Usage: ", Tone.Dim) + "qaas-playwright record <name> <url> "
                    + t.Paint("[--output-dir <dir>] [--keep-recording] [--no-panel] [--login <flow> --login-assembly <dll>]", Tone.Dim));
        t.WriteLine(Margin + t.Paint("Run ", Tone.Dim) + "qaas-playwright --help" + t.Paint(" for more.", Tone.Dim));
        t.WriteLine();
    }

    /// <summary>The version alone when the output goes to a script; with what it runs on, for a person.</summary>
    public static void PrintVersion()
    {
        var t = Out;
        if (Console.IsOutputRedirected)
        {
            t.WriteLine(Version);
            return;
        }

        var playwright = typeof(Microsoft.Playwright.IPlaywright).Assembly.GetName().Version?.ToString(3);
        t.WriteLine(t.Paint("qaas-playwright", Tone.Bold) + " " + t.Paint(Version, Tone.Accent));
        t.WriteLine(t.Paint(string.Join($" {t.Dot} ", $"Playwright {playwright}",
            $".NET {Environment.Version}", RuntimeInformation.RuntimeIdentifier), Tone.Dim));
    }

    // ── The questions ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The line before the questions.</summary>
    public static void Intro(string text)
    {
        Out.WriteLine();
        Out.WriteLine(Margin + Terminal.Clean(text));
        Out.WriteLine();
    }

    /// <summary>
    /// Asks until the answer is valid: <paramref name="problem"/> tells what is wrong with one, or null. An empty
    /// answer takes <paramref name="defaultValue"/>.
    /// </summary>
    /// <exception cref="OperationCanceledException">The input ended (Ctrl+D, or Ctrl+Z and Enter on Windows).</exception>
    public static string Ask(string prompt, string defaultValue, Func<string, string?>? problem = null)
    {
        var t = Out;
        while (true)
        {
            var hint = defaultValue.Length > 0 ? t.Paint($" ({defaultValue})", Tone.Dim) : "";
            t.Writer.Write(Margin + t.Paint("?", Tone.Accent) + " " + t.Paint(prompt, Tone.Bold) + hint
                           + t.Paint($" {t.Glyph("›", ">")} ", Tone.Dim) + t.Start(Tone.Accent));
            var typed = Console.ReadLine();
            t.Writer.Write(t.Stop);
            if (typed is null) throw new OperationCanceledException("The input ended.");

            var answer = typed.Trim().Length > 0 ? typed.Trim() : defaultValue;
            // Typed answers show by themselves; piped ones are shown, so a log reads as the questions went.
            if (Console.IsInputRedirected) t.WriteLine(t.Paint(Terminal.Clean(answer), Tone.Accent));
            if (problem?.Invoke(answer) is not { } wrong) return answer;
            Status(t, Margin + Margin, t.Glyph("×", "x"), Tone.Error, wrong, Tone.Error);
        }
    }

    /// <summary>The message of a refused argument, without the parameter's name .NET adds to it.</summary>
    public static string Reason(ArgumentException refused) =>
        refused.ParamName is { } name ? refused.Message.Replace($" (Parameter '{name}')", "") : refused.Message;

    /// <summary>Says goodbye on Ctrl+C instead of leaving a half-written line.</summary>
    public static void CancelOnCtrlC() => Console.CancelKeyPress += (_, _) => Cancelled();

    public static void Cancelled()
    {
        if (Interlocked.Exchange(ref _cancelled, 1) == 1) return;
        var t = Out;
        t.Writer.Write(t.Stop); // The colour of an answer being typed.
        t.WriteLine();
        t.WriteLine();
        Status(t, Margin, t.Glyph("×", "x"), Tone.Dim,
            Volatile.Read(ref _recording) ? "Cancelled. The recording was not saved." : "Cancelled.");
        t.WriteLine();
    }

    // ── While recording ─────────────────────────────────────────────────────────────────────────────────────────

    public static void Recording(RecordRequest request)
    {
        var t = Out;
        var flowPath = Path.Combine(request.OutputDir, $"{request.ClassName}.cs");
        lock (Gate)
        {
            t.WriteLine();
            Field(t, "URL", [new Span(request.Url, Shrink: Shrink.Middle, Untrusted: true)]);
            Field(t, "Flow", [new Span(request.ClassName, Tone.Bold, Untrusted: true),
                new Span($" {t.Glyph("→", "->")} ", Tone.Dim), new Span(Relative(flowPath), Shrink: Shrink.Middle, Untrusted: true)]);
            Keys(t, [("F9", "overlay"), ($"Alt+{t.Glyph("↑/↓", "Up/Down")}", "pick element"), ("F8", "pause"),
                ("F4", "freeze elements"), ("Enter", "or close Chrome to finish")]);
            t.WriteLine();
            RecordingState(t, "Recording", "Work through the site in Chrome; each action shows here.");
            t.WriteLine();
            StepOfAction.Clear();
            _lastStep = 0;
            Volatile.Write(ref _recording, true);
        }
    }

    /// <summary>Completes when the person presses Enter; never, when nobody can type here.</summary>
    public static Task EnterPressedAsync() =>
        Console.IsInputRedirected ? new TaskCompletionSource().Task : Task.Run(() => Console.ReadLine());

    /// <summary>Shows an action as it is captured, with what to check in it, and what the page did after it.</summary>
    public static void Captured(object news)
    {
        var t = Out;
        lock (Gate)
        {
            switch (news)
            {
                case CapturedAction action:
                    var number = StepOfAction.TryGetValue(action.Id, out var known) ? known : StepOfAction.Count + 1;
                    if (StepLine(t, number, action) is not { } line) return;
                    StepOfAction[action.Id] = _lastStep = number;
                    t.WriteLine(line);
                    foreach (var check in ChecksOf(action))
                        Status(t, new string(' ', StepIndent), t.Glyph("▲", "!"), Tone.Warning, check, Tone.Warning);
                    break;
                case CapturedEffect effect when EffectLine(t, effect) is { } effectLine:
                    t.WriteLine(effectLine);
                    break;
            }
        }
    }

    /// <summary>A stage of the work that takes a moment, such as "Logging in" or "Saving".</summary>
    public static void Phase(string text) => Status(Out, Margin, Out.Glyph("›", ">"), Tone.Accent, $"{text}{Out.Ellipsis}", Tone.Dim);

    public static void Paused() =>
        Status(Out, Margin, Out.Glyph("║", "="), Tone.Warning, "Paused. What you do now is not recorded.");

    public static void Resumed() => RecordingState(Out, "Recording", "resumed.");

    // ── One line of each kind ───────────────────────────────────────────────────────────────────────────────────

    public static void Info(string message) => Status(Out, Margin, " ", Tone.Plain, message);

    public static void Success(string message) =>
        Status(Out, Margin, Out.Glyph("✓", "√", "+"), Tone.Success, message);

    public static void Warning(string message) =>
        Status(Out, Margin, Out.Glyph("▲", "!"), Tone.Warning, message, Tone.Warning);

    public static void Error(string message)
    {
        var t = Terminal.Error;
        t.WriteLine();
        Status(t, Margin, t.Glyph("×", "x"), Tone.Error, message);
        t.WriteLine();
    }

    // ── The summary ─────────────────────────────────────────────────────────────────────────────────────────────

    public static void Saved(RecordRequest request, SavedFlow saved, GeneratedFlow flow)
    {
        var t = Out;
        lock (Gate)
        {
            Volatile.Write(ref _recording, false);
            var settings = flow.Yaml.Split('\n').Count(line => line.StartsWith("    ", StringComparison.Ordinal));
            var counts = new[]
                {
                    Count(flow.ActionLines.Count, "action"), Count(flow.StepLines.Count, "step"),
                    Count(settings, "setting"),
                    flow.Checks.Count > 0 ? $"{flow.Checks.Count} to check by hand" : "nothing to check by hand",
                };

            t.WriteLine();
            Rule(t);
            t.WriteLine();
            Headline(t, t.Paint(t.Glyph("✓", "√", "+"), Tone.Success) + " " + t.Paint("Saved", Tone.Bold) + " "
                        + t.Paint(Terminal.Clean(request.ClassName), Tone.Accent), string.Join($" {t.Dot} ", counts), together: true);
            t.WriteLine();

            var files = new List<(string Path, string What)>
            {
                (saved.CodePath, saved.CodePath.EndsWith(".cs", StringComparison.Ordinal) ? "the flow" : "the new recording"),
                (saved.YamlPath, "its settings"),
            };
            if (saved.RecordingPath is { } raw) files.Add((raw, "the raw recording"));
            var shown = files.Select(file => (Path: Terminal.Clean(Relative(file.Path)), file.What)).ToList();
            var pathWidth = shown.Max(file => Terminal.WidthOf(file.Path));
            foreach (var (path, what) in shown)
                t.WriteLine(t.Line([
                    new Span(Margin + Margin), new Span(path, Shrink: Shrink.Middle, Untrusted: true),
                    new Span(new string(' ', pathWidth - Terminal.WidthOf(path) + 3)), new Span(what, Tone.Dim, Shrink.End),
                ]));
            if (!saved.CodePath.EndsWith(".cs", StringComparison.Ordinal))
            {
                t.WriteLine();
                Warning($"{request.ClassName}.cs was kept as it is: merge the new recording into it by hand.");
            }

            if (flow.Checks.Count > 0) ChecksSection(t, saved.CodePath, flow.Checks);
            NextSection(t, request, flow);
        }
    }

    private static void ChecksSection(Terminal t, string codePath, IReadOnlyList<CheckByHand> checks)
    {
        Section(t, "Check by hand", (t.Glyph("▲", "!"), Tone.Warning));
        var file = Relative(codePath);
        var places = checks.Select(check => check.Line is { } line ? $"{file}:{line}" : file).ToList();
        var placeWidth = places.Max(place => Terminal.WidthOf(Terminal.Clean(place)));
        var indent = Margin.Length * 2 + placeWidth + 2;
        for (var index = 0; index < checks.Count; index++)
        {
            var lines = Terminal.Wrap(Terminal.Clean(checks[index].Message), Math.Max(30, t.Width - 1 - indent)).ToList();
            var place = Terminal.Clean(places[index]);
            t.WriteLine(Margin + Margin + t.Paint(place, Tone.Warning)
                        + new string(' ', placeWidth - Terminal.WidthOf(place) + 2) + lines[0]);
            foreach (var more in lines.Skip(1)) t.WriteLine(new string(' ', indent) + more);
        }
    }

    private static void NextSection(Terminal t, RecordRequest request, GeneratedFlow flow)
    {
        Section(t, "Next");
        var values = flow.Yaml.Split('\n').Where(line => line.Length > 0 && !line.StartsWith('#')).Select(line => "    " + line);
        var yaml = (string[])
        [
            "Sessions:",
            "  - Name: MySession",
            "    Probes:",
            $"      - Name: {request.ClassName}",
            "        Probe: PlaywrightFlowProbe",
            "        ProbeConfiguration:",
            $"          BaseUrl: '{request.Url.Replace("'", "''")}'",
            $"          Flows: [{request.ClassName}]",
            .. values.Select(line => "      " + line),
        ];
        t.WriteLine(Margin + Margin + t.Paint("1", Tone.Accent) + "  Add the probe to your " + t.Paint("test.qaas.yaml", Tone.Bold));
        t.WriteLine();
        foreach (var line in yaml) t.WriteLine(new string(' ', 7) + Yaml(t, line));
        t.WriteLine();
        t.WriteLine(Margin + Margin + t.Paint("2", Tone.Accent) + "  Run it");
        t.WriteLine();
        t.WriteLine(new string(' ', 7) + t.Paint("$ ", Tone.Dim) + "dotnet run -- run test.qaas.yaml");
        t.WriteLine();
    }

    // ── A step and what it caused ───────────────────────────────────────────────────────────────────────────────

    /// <summary>The line of a step: its number, glyph and verb, the element's name and value, and its locator, dim.</summary>
    internal static string? StepLine(Terminal t, int number, CapturedAction action)
    {
        if (action is { Kind: "click", Widget: "select-open" or "autocomplete-button" }) return null;

        var element = action.Element;
        var locator = element is null ? null : LocatorPolicy.Describe(LocatorPolicy.Choose(element, byName: action.Widget == "radio"));
        var name = Terminal.Clean(element?.Text) is { Length: > 0 } text ? text
            : Terminal.Clean(element?.Label) is { Length: > 0 } label ? label
            : null;
        if (name is null)
        {
            // An element without a name is known by its locator.
            name = locator ?? element?.Tag ?? "";
            locator = null;
        }

        var (glyph, verb) = Verb(t, action);
        var arrow = t.Glyph("→", "->");
        var value = Value(t, action);
        List<Span> subject = action.Kind switch
        {
            "navigate" or "newTab" => [Untrusted(action.Url ?? "", Tone.Bold, Shrink.Middle, order: 2)],
            "fill" => [Untrusted(name, Tone.Bold, order: 0, min: 24), new Span(" = ", Tone.Dim), value],
            "choose" => [Untrusted(name, Tone.Bold, order: 0, min: 24), new Span($" {arrow} ", Tone.Dim), value],
            "upload" => [Untrusted(name, Tone.Bold, order: 0, min: 24), new Span($" {t.Glyph("←", "<-")} ", Tone.Dim), value],
            "drag" => [Untrusted(name, Tone.Bold, order: 0, min: 24), new Span($" {arrow} ", Tone.Dim),
                Untrusted(Terminal.Clean(action.To?.Text) is { Length: > 0 } to ? to : "?", order: 2)],
            "press" when action.OnPage == true || element is null => [new Span(action.Key ?? "", Tone.Bold)],
            "press" => [new Span(action.Key ?? "", Tone.Bold), new Span(" in ", Tone.Dim), Untrusted(name, order: 0, min: 24)],
            _ => [Untrusted(name, Tone.Bold, order: 0, min: 24)],
        };
        if (action.Kind is "navigate" or "newTab" || action is { Kind: "press", OnPage: true }) locator = null;

        List<Span> spans =
        [
            new(Margin), new(number.ToString().PadLeft(NumberWidth), Tone.Dim), new(" "), new(glyph, Tone.Accent),
            new(" "), new(verb.PadRight(VerbWidth)),
            .. subject,
        ];
        // A narrow terminal shows the locator only in the panel and the overlay.
        if (locator is not null && t.Width - 1 - StepIndent - VerbWidth >= 40)
        {
            var used = spans.Sum(span => Terminal.WidthOf(span.Untrusted ? Terminal.Clean(span.Text) : span.Text));
            var subjectWidth = used - StepIndent - VerbWidth;
            // Locators start in one column while they have room there, which a narrow terminal moves left.
            var column = Math.Min(SubjectWidth, (t.Width - 1 - StepIndent - VerbWidth) * 11 / 20);
            var aligned = Math.Max(2, column - subjectWidth);
            var room = t.Width - 1 - used - aligned;
            spans.Add(new Span(new string(' ', room >= Math.Min(LeastLocator, Terminal.WidthOf(Terminal.Clean(locator))) ? aligned : 2)));
            spans.Add(Untrusted(locator, Tone.Dim, order: 1, min: 12));
        }

        return t.Line(spans);
    }

    /// <summary>What a person should look at in the step's code, as "Check by hand" will list it when saving.</summary>
    internal static IEnumerable<string> ChecksOf(CapturedAction action)
    {
        // As the flow writes them: a key pressed on the page, or with no element, goes to the keyboard.
        var located = action.Kind is not ("navigate" or "newTab")
                      && !(action.Kind == "press" && (action.OnPage == true || action.Element is null));
        if (located && LocatorPolicy.Problem(LocatorPolicy.Choose(action.Element, byName: action.Widget == "radio")) is { } problem)
            yield return problem;
        if (action.HoverMenu == true) yield return "the menu opened on hover: add HoverAsync() on what opens it";
        if (action.Kind == "upload") yield return "the flow needs each file's full path: return it in FileToUpload";
        if (action.Kind == "drag") yield return "drag and drop: check where it drops";
    }

    private static string? EffectLine(Terminal t, CapturedEffect effect)
    {
        var (what, detail) = effect.Kind switch
        {
            "dialogOpened" => ("opened dialog", effect.Dialog?.Title),
            "dialogClosed" => ("closed dialog", effect.Dialog?.Title),
            "alert" => ("alert", effect.Text),
            "tabSelected" => ("selected the tab", null),
            "urlChanged" => ("went to", effect.Url),
            "popup" => ("opened a pop-up", effect.Url),
            "jsDialog" => ("browser dialog", effect.Text),
            _ => ((string?)null, (string?)null),
        };
        if (what is null) return null;

        // Said with its step when another step was shown since.
        var step = effect.ActionId is not null && StepOfAction.TryGetValue(effect.ActionId, out var number) ? number : 0;
        if (step > 0 && step != _lastStep) what = $"step {step} {what}";
        return t.Line([
            new Span(new string(' ', StepIndent)), new Span(t.Glyph("└", "`-") + " ", Tone.Dim), new Span(what, Tone.Dim),
            .. Terminal.Clean(detail) is { Length: > 0 } shown
                ? (Span[])[new Span("  "), Untrusted(shown, shrink: effect.Kind == "urlChanged" ? Shrink.Middle : Shrink.End)]
                : [],
        ]);
    }

    private static (string Glyph, string Verb) Verb(Terminal t, CapturedAction action) => action.Kind switch
    {
        "click" => (t.Glyph("•", "*"), "click"),
        "dblclick" => (t.Glyph("•", "*"), "dblclick"),
        "fill" => (t.Glyph("≡", "="), "fill"),
        "choose" => (t.Glyph("▼", "v"), "choose"),
        "check" => (t.Glyph("■", "x"), "check"),
        "uncheck" => (t.Glyph("□", "o"), "uncheck"),
        "press" => (t.Glyph("◘", "#"), "press"),
        "upload" => (t.Glyph("↑", "^"), "upload"),
        "drag" => (t.Glyph("↔", "~"), "drag"),
        "navigate" => (t.Glyph("→", ">"), "go to"),
        "newTab" => (t.Glyph("+", "+"), "new tab"),
        var other => (t.Glyph("•", "*"), other),
    };

    // A password is never shown, as someone may be watching the screen.
    private static Span Value(Terminal t, CapturedAction action)
    {
        if (action.Kind == "fill" && action.Element?.Type == "password") return new Span(new string(t.Glyph("•", "*")[0], 8), Tone.Accent);
        var value = action.Kind == "upload" ? string.Join(", ", action.Files ?? []) : action.Value ?? "";
        return value.Length == 0 ? new Span("\"\"", Tone.Dim) : Untrusted(value, Tone.Accent, Shrink.Middle, order: 2, min: 8);
    }

    private static Span Untrusted(string text, Tone tone = Tone.Plain, Shrink shrink = Shrink.End, int order = 1, int min = 6) =>
        new(text, tone, shrink, order, min, Untrusted: true);

    // ── Drawing ─────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A glyph and a message, wrapped to the terminal's width under the message's start. Each line of a message with
    /// several is wrapped on its own.
    /// </summary>
    private static void Status(Terminal t, string indent, string glyph, Tone glyphTone, string message, Tone tone = Tone.Plain)
    {
        var start = indent.Length + Terminal.WidthOf(glyph) + 1;
        var width = Math.Max(20, t.Width - 1 - start);
        var lines = message.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0)
            .SelectMany(line => Terminal.Wrap(line, width)).DefaultIfEmpty("").ToList();
        lock (Gate)
        {
            t.WriteLine(indent + t.Paint(glyph, glyphTone) + " " + t.Paint(lines[0], tone));
            foreach (var more in lines.Skip(1)) t.WriteLine(new string(' ', start) + t.Paint(more, tone));
        }
    }

    private static void RecordingState(Terminal t, string state, string detail) =>
        Headline(t, t.Paint(t.Glyph("●", "*"), Tone.Error) + " " + t.Paint(state, Tone.Bold), detail);

    // A bold first line with dim details after it, wrapped under the details' start when they do not fit; details
    // kept together go on the next line instead.
    private static void Headline(Terminal t, string headline, string details, bool together = false)
    {
        var start = Margin.Length + Terminal.WidthOf(headline) + 2;
        var oneLine = start + Terminal.WidthOf(details) <= t.Width - 1;
        if (!oneLine && (together || t.Width - 1 - start < 30))
        {
            t.WriteLine(Margin + headline);
            start = Margin.Length + 2;
            foreach (var line in Terminal.Wrap(details, t.Width - 1 - start)) t.WriteLine(new string(' ', start) + t.Paint(line, Tone.Dim));
            return;
        }

        var lines = Terminal.Wrap(details, t.Width - 1 - start).ToList();
        t.WriteLine(Margin + headline + "  " + t.Paint(lines[0], Tone.Dim));
        foreach (var more in lines.Skip(1)) t.WriteLine(new string(' ', start) + t.Paint(more, Tone.Dim));
    }

    private static void Field(Terminal t, string label, IEnumerable<Span> value) =>
        t.WriteLine(t.Line([new Span(Margin), new Span(label.PadRight(6), Tone.Dim), .. value]));

    // The keys on one line, or on more under each other when the terminal is narrow.
    private static void Keys(Terminal t, IReadOnlyList<(string Key, string Does)> keys)
    {
        var separator = $"  {t.Dot}  ";
        var line = Margin + t.Paint("Keys".PadRight(6), Tone.Dim);
        var width = Margin.Length + 6;
        foreach (var (key, does) in keys)
        {
            var item = Terminal.WidthOf(key) + 1 + Terminal.WidthOf(does);
            var first = width == Margin.Length + 6;
            if (!first && width + separator.Length + item > t.Width - 1)
            {
                t.WriteLine(line);
                (line, width, first) = (new string(' ', Margin.Length + 6), Margin.Length + 6, true);
            }

            line += (first ? "" : t.Paint(separator, Tone.Dim)) + t.Paint(key, Tone.Accent) + " " + does;
            width += (first ? 0 : separator.Length) + item;
        }

        t.WriteLine(line);
    }

    private static void Section(Terminal t, string title, (string Text, Tone Tone)? glyph = null)
    {
        t.WriteLine();
        t.WriteLine(Margin + (glyph is var (text, tone) ? t.Paint(text, tone) + " " : "") + t.Paint(title, Tone.Bold));
    }

    // A section of the help: each command, option or key, then what it does, from the column on.
    private static void Rows(Terminal t, string title, IReadOnlyList<(string What, string Does)> rows, int column,
        Tone tone = Tone.Accent)
    {
        Section(t, title);
        foreach (var (what, does) in rows)
        {
            var lines = Terminal.Wrap(does, Math.Max(24, t.Width - 1 - column)).ToList();
            t.WriteLine(Margin + Margin + t.Paint(what, tone) + new string(' ', column - Margin.Length * 2 - Terminal.WidthOf(what))
                        + lines[0]);
            foreach (var more in lines.Skip(1)) t.WriteLine(new string(' ', column) + more);
        }
    }

    private static int ColumnFor(IEnumerable<(string What, string Does)> rows) =>
        Margin.Length * 2 + rows.Max(row => Terminal.WidthOf(row.What)) + 4;

    private static void Rule(Terminal t) =>
        t.WriteLine(Margin + t.Paint(new string(t.Glyph("─", "-")[0], Math.Min(t.Width - 1, 80) - Margin.Length * 2), Tone.Dim));

    // A line of YAML with its key coloured.
    private static string Yaml(Terminal t, string line)
    {
        var colon = line.IndexOf(':');
        if (colon < 0) return line;
        var lead = line.Length - line.TrimStart(' ', '-').Length;
        return line[..lead] + t.Paint(line[lead..colon], Tone.Accent) + line[colon..];
    }

    private static string Count(int count, string what) => $"{count} {what}{(count == 1 ? "" : "s")}";

    // Paths as the person would type them from here; absolute when elsewhere.
    private static string Relative(string path)
    {
        var full = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, full);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? full : relative;
    }

    // ── The login flow, pausing and freezing ────────────────────────────────────────────────────────────────────

    public static void LoggingIn(Login.LoginFlow login, bool remembered)
    {
        Phase($"Logging in with {login.Flow} from {Path.GetFileName(login.Assembly)}");
        if (remembered) Status(Out, Margin, " ", Tone.Plain, "as last time in this folder; --no-login records without it", Tone.Dim);
    }

    public static void LoggedIn(string flow, string url) => Success($"Logged in with {flow}. Recording from {url}");

    public static void LoginFailed(string flow, string reason)
    {
        Error($"{flow} failed: {reason}");
        Info("Nothing is recorded yet. Log in by hand in the browser, then press F8 there (or Resume in the panel) to " +
             "start recording. To quit, close the browser.");
    }

    /// <summary>A line the login flow logged.</summary>
    public static void FlowLog(string message) => Status(Out, Margin + Margin, " ", Tone.Plain, message, Tone.Dim);

    public static void StateChanged(Capture.RecordingState state)
    {
        var before = _state;
        _state = state;
        // After a test, the recording goes on as before it, and the test's result says so.
        if (before == Capture.RecordingState.Testing) return;
        if (state == Capture.RecordingState.Paused) Paused();
        else if (state == Capture.RecordingState.Recording) Resumed();
    }

    public static void Froze(FrozenElements frozen) =>
        Status(Out, Margin, Out.Glyph("❄", "*"), Tone.Accent,
            $"The elements of {frozen.Page} are frozen: pick them in the panel's Elements tab.", Tone.Dim);

    // ── Testing the flow ────────────────────────────────────────────────────────────────────────────────────────

    public static void Testing(TestRun.TestReport report) =>
        Phase(report.UpTo is null
            ? $"Testing {report.Flow}"
            : $"Testing {report.Flow} up to step {report.Total} of {report.Actions.Count}");

    /// <summary>
    /// How a test went, in a line or a few: passed, with how long it took; or the step that failed, as its line shows
    /// while recording, why, as the probe says it, and what to check.
    /// </summary>
    public static void Tested(TestRun.TestReport report, Recording recording)
    {
        var t = Out;
        var took = report.Ms is { } ms ? $"{ms / 1000.0:0.0} s" : null;
        var steps = report.Total == report.Actions.Count
            ? Count(report.Total, "step")
            : $"{report.Total} of {report.Actions.Count} steps";
        lock (Gate)
        {
            switch (report.Outcome)
            {
                case TestRun.TestOutcome.Passed:
                    Success($"{report.Flow} passed: {steps}{(took is null ? "" : $" in {took}")}.");
                    break;
                case TestRun.TestOutcome.Stopped:
                    Status(t, Margin, t.Glyph("■", "#"), Tone.Dim, $"Stopped testing {report.Flow}.", Tone.Dim);
                    break;
                case TestRun.TestOutcome.Failed when report.Failure is { } failure:
                    Failed(t, report, failure, recording, took);
                    break;
                default:
                    Status(t, Margin, t.Glyph("×", "x"), Tone.Error, $"Could not test {report.Flow}: {report.Problem}");
                    break;
            }
        }
    }

    private static void Failed(Terminal t, TestRun.TestReport report, TestRun.TestFailure failure, Recording recording, string? took)
    {
        var number = failure.Action is { } id ? report.Actions.TakeWhile(action => action != id).Count() + 1 : 0;
        var where = failure.InLogin is { } login ? $"in {login}, before its first step"
            : number > 0 ? $"at step {number} of {report.Actions.Count}{(failure.Line is { } line ? $", line {line}" : "")}"
            : "before its first step";
        Status(t, Margin, t.Glyph("×", "x"), Tone.Error, $"{report.Flow} failed {where}{(took is null ? "" : $", after {took}")}.");
        var indent = new string(' ', StepIndent);
        if (recording.Actions.FirstOrDefault(action => action.Id == failure.Action) is { } failed && StepLine(t, number, failed) is { } step)
            t.WriteLine(step);
        Status(t, indent, t.Glyph("└", "`-"), Tone.Dim, Terminal.Clean(failure.Headline), Tone.Error);
        foreach (var check in failure.Checks) Status(t, indent, t.Glyph("▲", "!"), Tone.Warning, check, Tone.Warning);
        foreach (var hint in failure.Hints) Status(t, indent, " ", Tone.Plain, hint, Tone.Dim);
    }

    /// <summary>After <see cref="Saved"/>, for a flow recorded after a login flow.</summary>
    public static void RunAfter(string loginFlow) =>
        Info($"It was recorded after {loginFlow}: run that first, with SetupFlows: [{loginFlow}] and its settings.");
}
