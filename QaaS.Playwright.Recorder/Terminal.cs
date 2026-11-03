using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace QaaS.Playwright.Recorder;

/// <summary>The colours of the recorder's console, by what they mean.</summary>
internal enum Tone
{
    Plain,
    Bold,
    Dim,
    Accent,
    Success,
    Warning,
    Error,

    /// <summary>The product's name, in a coloured badge.</summary>
    Badge,
}

/// <summary>How a piece of a line gives way when the line is wider than the terminal.</summary>
internal enum Shrink
{
    Never,

    /// <summary>Cut at the end: <c>button "Save ord…</c>.</summary>
    End,

    /// <summary>Cut in the middle, keeping both ends: <c>https://my-app.com/…/orders/5</c>.</summary>
    Middle,
}

/// <summary>
/// A piece of a line: its text, colour, and how it shrinks. Pieces with a lower <paramref name="Order"/> shrink first,
/// down to <paramref name="Min"/> columns. <paramref name="Untrusted"/> text came from a page or a person: its control
/// characters are removed, and right-to-left text in it is isolated so it cannot reorder the rest of the line.
/// </summary>
internal readonly record struct Span(
    string Text, Tone Tone = Tone.Plain, Shrink Shrink = Shrink.Never, int Order = 0, int Min = 1, bool Untrusted = false);

/// <summary>
/// What a console can show, and the text helpers that keep the recorder's lines tidy in it: colours, glyphs with ASCII
/// fallbacks, its width, and right-to-left names that stay in place. Colours are off when the output is redirected,
/// when <c>NO_COLOR</c> is set or <c>TERM</c> is <c>dumb</c>, and on when <c>FORCE_COLOR</c> is set.
/// </summary>
internal sealed class Terminal
{
    private const char Escape = '\u001b';
    private const string Reset = "\u001b[0m";

    // First strong isolate and pop directional isolate: the text between them is laid out on its own.
    private const char Isolate = '\u2068';
    private const char PopIsolate = '\u2069';

    // When the width is unknown (output to a file), lines are not cut.
    private const int Unlimited = 100_000;

    private static readonly Lazy<Terminal> StandardOutput = new(() => Detect(error: false));
    private static readonly Lazy<Terminal> StandardError = new(() =>
    {
        _ = StandardOutput.Value; // It sets the console's encoding on Windows, which tells whether glyphs show.
        return Detect(error: true);
    });

    private readonly Func<TextWriter> _writer;
    private readonly Func<int> _width;

    private Terminal(Func<TextWriter> writer, Func<int> width, bool color, bool unicode, bool richGlyphs, bool isolates)
    {
        _writer = writer;
        _width = width;
        Color = color;
        Unicode = unicode;
        RichGlyphs = richGlyphs;
        Isolates = isolates;
    }

    /// <summary>The recorder's standard output.</summary>
    public static Terminal Out => StandardOutput.Value;

    /// <summary>The standard error, where errors go.</summary>
    public static Terminal Error => StandardError.Value;

    public TextWriter Writer => _writer();

    /// <summary>Its width in columns; very large when unknown.</summary>
    public int Width => Math.Max(20, _width());

    public bool Color { get; }

    /// <summary>Glyphs every console font has (WGL4: box drawing, arrows, ●, ▲); else ASCII.</summary>
    public bool Unicode { get; }

    /// <summary>Also glyphs beyond WGL4, like ✓, which the legacy Windows console font may lack.</summary>
    public bool RichGlyphs { get; }

    /// <summary>Wraps right-to-left text in directional isolates, for terminals that lay out bidirectional text.</summary>
    public bool Isolates { get; }

    /// <summary>A terminal of a fixed width writing to <paramref name="writer"/>, for tests.</summary>
    public static Terminal Fixed(TextWriter writer, int width, bool color = false, bool unicode = true, bool isolates = true) =>
        new(() => writer, () => width, color, unicode, unicode, isolates);

    public string Glyph(string unicode, string ascii) => Unicode ? unicode : ascii;

    /// <summary>A glyph with a fallback for the legacy Windows console's fonts, then for ASCII.</summary>
    public string Glyph(string rich, string wgl4, string ascii) => RichGlyphs ? rich : Unicode ? wgl4 : ascii;

    public string Ellipsis => Glyph("…", "...");

    public string Dot => Glyph("·", "|");

    public string Paint(string text, Tone tone) =>
        !Color || tone == Tone.Plain || text.Length == 0 ? text : $"{Start(tone)}{text}{Reset}";

    /// <summary>Starts a colour that lasts until <see cref="Stop"/>, as for what the person types.</summary>
    public string Start(Tone tone) => Color ? $"{Escape}[{Sgr(tone)}m" : "";

    public string Stop => Color ? Reset : "";

    public void WriteLine(string line = "") => Writer.WriteLine(line);

    /// <summary>The spans as one line, shrunk to the terminal's width and painted.</summary>
    public string Line(IReadOnlyList<Span> spans)
    {
        var texts = spans.Select(span => span.Untrusted ? Clean(span.Text) : span.Text).ToArray();
        var widths = texts.Select(WidthOf).ToArray();
        // One column is kept free: writing the last column wraps the cursor early in some consoles.
        var overflow = widths.Sum() - (Width - 1);
        var shrinking = Enumerable.Range(0, spans.Count).Where(index => spans[index].Shrink != Shrink.Never)
            .OrderBy(index => spans[index].Order).ToList();
        // Each piece down to its least first, then further when the line still does not fit.
        foreach (var least in (bool[])[true, false])
            foreach (var index in shrinking)
            {
                if (overflow <= 0) break;
                var floor = least ? Math.Min(spans[index].Min, widths[index]) : 1;
                texts[index] = Fit(texts[index], Math.Max(floor, widths[index] - overflow), spans[index].Shrink == Shrink.Middle);
                overflow -= widths[index] - WidthOf(texts[index]);
                widths[index] = WidthOf(texts[index]);
            }

        var line = new StringBuilder();
        for (var index = 0; index < spans.Count; index++)
            line.Append(Paint(spans[index].Untrusted ? IsolateRtl(texts[index]) : texts[index], spans[index].Tone));
        return line.ToString();
    }

    /// <summary>Cuts <paramref name="text"/> to <paramref name="width"/> columns, with an ellipsis where it was cut.</summary>
    public string Fit(string text, int width, bool middle = false)
    {
        if (WidthOf(text) <= width) return text;
        var room = width - WidthOf(Ellipsis);
        if (room <= 0) return Take(text, Math.Max(0, width));
        if (!middle) return Take(text, room).TrimEnd() + Ellipsis;
        var head = Take(text, (room + 1) / 2);
        return head + Ellipsis + TakeLast(text, room - WidthOf(head));
    }

    /// <summary>Splits <paramref name="text"/> into lines of at most <paramref name="width"/> columns, between words.</summary>
    public static IEnumerable<string> Wrap(string text, int width)
    {
        var line = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && WidthOf(line.ToString()) + 1 + WidthOf(word) > width)
            {
                yield return line.ToString();
                line.Clear();
            }

            line.Append(line.Length > 0 ? " " : "").Append(word);
        }

        if (line.Length > 0) yield return line.ToString();
    }

    /// <summary>
    /// Text from a page or a person, safe to print: control characters (a page could send escape sequences) become a
    /// space, runs of white space one space, and directional formatting characters are removed.
    /// </summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var clean = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (IsDirectionalFormatting(character)) continue;
            var space = char.IsControl(character) || char.IsWhiteSpace(character);
            if (space && (clean.Length == 0 || clean[^1] == ' ')) continue;
            clean.Append(space ? ' ' : character);
        }

        return clean.ToString().TrimEnd();
    }

    /// <summary>The columns <paramref name="text"/> takes, without its colour codes.</summary>
    public static int WidthOf(string text)
    {
        var width = 0;
        for (var index = 0; index < text.Length;)
        {
            if (text[index] == Escape)
            {
                index = SkipEscape(text, index);
                continue;
            }

            var rune = Rune.GetRuneAt(text, index);
            width += WidthOf(rune);
            index += rune.Utf16SequenceLength;
        }

        return width;
    }

    public static bool HasRightToLeft(string text) => text.Any(character =>
        character is >= '\u0590' and <= '\u08FF' or >= '\uFB1D' and <= '\uFDFF' or >= '\uFE70' and <= '\uFEFF');

    private string IsolateRtl(string text) => Isolates && HasRightToLeft(text) ? $"{Isolate}{text}{PopIsolate}" : text;

    private static string Sgr(Tone tone) => tone switch
    {
        // 256-colour codes, which look the same in every theme: the 16 basic colours are themed, and Windows
        // PowerShell's own console makes dark yellow nearly white and dark magenta its blue background.
        Tone.Bold => "1",
        Tone.Dim => "38;5;244",
        Tone.Accent => "38;5;69",
        Tone.Success => "38;5;35",
        Tone.Warning => "38;5;172",
        Tone.Error => "38;5;167",
        Tone.Badge => "1;38;5;231;48;5;62",
        _ => "0",
    };

    private static string Take(string text, int width)
    {
        var taken = new StringBuilder();
        var used = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (used + WidthOf(rune) > width) break;
            used += WidthOf(rune);
            taken.Append(rune.ToString());
        }

        return taken.ToString();
    }

    private static string TakeLast(string text, int width)
    {
        var runes = text.EnumerateRunes().ToList();
        var used = 0;
        var start = runes.Count;
        while (start > 0 && used + WidthOf(runes[start - 1]) <= width) used += WidthOf(runes[--start]);
        return string.Concat(runes.Skip(start).Select(rune => rune.ToString()));
    }

    // A CSI sequence: ESC [ parameters final-byte.
    private static int SkipEscape(string text, int index)
    {
        index++;
        if (index < text.Length && text[index] == '[')
            for (index++; index < text.Length && text[index] is < '@' or > '~'; index++) { }
        return index + 1;
    }

    private static int WidthOf(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format
            or UnicodeCategory.Control)
            return 0;
        var value = rune.Value;
        var wide = value is >= 0x1100 and <= 0x115F or >= 0x2E80 and <= 0x303E or >= 0x3041 and <= 0x33FF
            or >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF or >= 0xA000 and <= 0xA4CF or >= 0xAC00 and <= 0xD7A3
            or >= 0xF900 and <= 0xFAFF or >= 0xFE30 and <= 0xFE4F or >= 0xFF00 and <= 0xFF60 or >= 0xFFE0 and <= 0xFFE6
            or >= 0x1F300 and <= 0x1F64F or >= 0x1F900 and <= 0x1F9FF or >= 0x20000 and <= 0x3FFFD;
        return wide ? 2 : 1;
    }

    private static bool IsDirectionalFormatting(char character) =>
        character is '\u061C' or '\u200E' or '\u200F' or >= '\u202A' and <= '\u202E' or >= '\u2066' and <= '\u2069';

    private static Terminal Detect(bool error)
    {
        var redirected = error ? Console.IsErrorRedirected : Console.IsOutputRedirected;
        var term = Environment.GetEnvironmentVariable("TERM");
        var dumb = term == "dumb";
        var windows = OperatingSystem.IsWindows();
        var windowsTerminal = Environment.GetEnvironmentVariable("WT_SESSION") is { Length: > 0 };
        var ansi = !redirected && !dumb && (!windows || WindowsConsole.EnableAnsi(error));
        var color = Environment.GetEnvironmentVariable("NO_COLOR") is not { Length: > 0 }
                    && (ansi || Environment.GetEnvironmentVariable("FORCE_COLOR") is { Length: > 0 } force && force != "0");

        if (windows && !error && !Console.IsOutputRedirected && !Console.IsErrorRedirected) WindowsConsole.WriteUnicode();
        var unicode = !dumb && Console.OutputEncoding.CodePage is 65001 or 1200 or 1201 or 12000 or 12001;
        var rich = unicode && (!windows || windowsTerminal || Environment.GetEnvironmentVariable("TERM_PROGRAM") is not null);
        // Windows consoles lay out right-to-left text left to right, and the legacy one draws the isolates as boxes.
        var isolates = unicode && !windows && !windowsTerminal;

        Func<int> width = redirected
            ? () => int.TryParse(Environment.GetEnvironmentVariable("COLUMNS"), out var columns) ? columns : Unlimited
            : () =>
            {
                try
                {
                    return Console.WindowWidth is > 0 and var columns ? columns : 80;
                }
                catch (IOException)
                {
                    return 80;
                }
            };
        return new Terminal(error ? () => Console.Error : () => Console.Out, width, color, unicode, rich, isolates);
    }

    /// <summary>Windows consoles show colour codes and every character only when asked.</summary>
    private static class WindowsConsole
    {
        private const int StdOutputHandle = -11;
        private const int StdErrorHandle = -12;
        private const uint EnableVirtualTerminalProcessing = 0x0004;

        /// <summary>Turns on colour codes in the console; false when it cannot show them (before Windows 10).</summary>
        public static bool EnableAnsi(bool error)
        {
            try
            {
                var handle = GetStdHandle(error ? StdErrorHandle : StdOutputHandle);
                return GetConsoleMode(handle, out var mode)
                       && ((mode & EnableVirtualTerminalProcessing) != 0
                           || SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing));
            }
            catch (Exception failure) when (failure is DllNotFoundException or EntryPointNotFoundException)
            {
                return false;
            }
        }

        /// <summary>
        /// Writes UTF-16 to the console itself, so every character shows whatever its code page; unlike UTF-8, this
        /// leaves the code page of the shell that started the recorder as it was.
        /// </summary>
        public static void WriteUnicode()
        {
            try
            {
                Console.OutputEncoding = Encoding.Unicode;
            }
            catch (IOException)
            {
                // Not a console after all: the default encoding stays, and glyphs fall back to ASCII.
            }
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int handle);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetConsoleMode(IntPtr handle, uint mode);
    }
}
