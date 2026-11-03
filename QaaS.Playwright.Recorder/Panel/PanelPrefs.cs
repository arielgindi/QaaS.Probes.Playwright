using System.Text.Encodings.Web;
using System.Text.Json;
using QaaS.Playwright.Browser;

namespace QaaS.Playwright.Recorder.Panel;

/// <summary>
/// What the person chose in the panel, kept between recordings in <c>~/.qaas/recorder-panel.json</c>: the theme, slow
/// motion, the panes' sizes, the hints already seen. The panel's page has no origin of its own, so no storage either:
/// it sends each choice, and gets them all back in the page as it opens.
/// </summary>
internal sealed class PanelPrefs
{
    // Only these, each short, so a page cannot fill the file.
    private static readonly HashSet<string> Known =
        ["theme", "slowMotion", "compactTree", "stepsWidth", "inspectorWidth", "codeShare", "seenPalette"];

    private const int LongestValue = 16;

    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.Default };

    private readonly string _path;
    private readonly Dictionary<string, string> _values;

    private PanelPrefs(string path, Dictionary<string, string> values) => (_path, _values) = (path, values);

    /// <summary>Where they are kept: <c>~/.qaas/recorder-panel.json</c>, or another file for the recorder's tests.</summary>
    internal static string FilePath { get; set; } = Path.Combine(Path.GetDirectoryName(BrowserDefaults.AuthStatePath)!, "recorder-panel.json");

    /// <summary>The choices kept; none when there is no file, or it cannot be read.</summary>
    public static PanelPrefs Load()
    {
        var path = FilePath;
        try
        {
            var read = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
            return new PanelPrefs(path, read.Where(pair => IsValid(pair.Key, pair.Value)).ToDictionary());
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            return new PanelPrefs(path, []);
        }
    }

    /// <summary>
    /// The panel's page with the choices in it, as <c>window.__qaasPanelPrefs</c>, before its own script runs. The JSON
    /// escapes <c>&lt;</c>, so nothing in it can end the script.
    /// </summary>
    public string Into(string html)
    {
        var script = $"<script>window.__qaasPanelPrefs={JsonSerializer.Serialize(_values, Json)};</script>";
        var head = html.IndexOf("<head>", StringComparison.Ordinal);
        return head < 0 ? script + html : html.Insert(head + "<head>".Length, script);
    }

    /// <summary>Keeps a choice; one the panel does not make, or that cannot be written, is let go.</summary>
    public async Task SetAsync(string? name, string? value)
    {
        if (name is null || value is null || !IsValid(name, value)) return;
        lock (_values)
        {
            if (_values.TryGetValue(name, out var was) && was == value) return;
            if (value.Length == 0) _values.Remove(name);
            else _values[name] = value;
        }

        try
        {
            string text;
            lock (_values) text = JsonSerializer.Serialize(_values, Json);
            await AtomicFileWriter.WriteAsync(_path, text);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // A choice not kept lasts until the panel closes.
        }
    }

    private static bool IsValid(string name, string value) =>
        Known.Contains(name) && value.Length <= LongestValue && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-');
}
