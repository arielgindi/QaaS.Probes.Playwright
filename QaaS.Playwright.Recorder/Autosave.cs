using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QaaS.Playwright.Browser;
using QaaS.Playwright.Configuration;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Generation;
using QaaS.Playwright.Recorder.Login;

namespace QaaS.Playwright.Recorder;

/// <summary>A recording the recorder kept, when it last changed, and its file.</summary>
internal sealed record KeptRecording(Recording Recording, DateTime Changed, string Path);

/// <summary>
/// The recorder's own copy of each recording, in <c>~/.qaas/recordings</c>, so that no work is lost and a saved flow can
/// be continued. While recording, it is written after every change as <c>&lt;Flow&gt;-&lt;key&gt;.unsaved.json</c>, the
/// key telling the folder the flow is saved in, so two projects' flows of one name do not meet; once the flow is saved,
/// as <c>&lt;Flow&gt;-&lt;key&gt;.json</c>, of which the newest 30 are kept. Each file is the recording as
/// <c>--keep-recording</c> writes it, with the flow it is of and the person's checks and edits, written atomically and
/// for its owner's eyes only, without the values the recorder hides: a password typed, which the console never shows,
/// and a value typed into a field whose setting is named as a secret (password, secret, token, API key, pwd), or that
/// the person named so, which a test run hides; a default the person gave such a setting neither.
/// </summary>
internal sealed class Autosave
{
    private const int SavedKept = 30;
    private const string Unsaved = ".unsaved.json";
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);
    private static string? _folder;

    private string? _written;
    private string? _path;
    private bool _created;
    private bool _warned;

    /// <param name="continued">The recording continued, as it was loaded; it is written again only once it changed.</param>
    /// <param name="request">The flow it is of.</param>
    public Autosave(Recording? continued = null, RecordRequest? request = null) =>
        _written = continued is null || request is null ? null : Stored(continued, request).ToJson();

    /// <summary>Where the recordings are kept: <c>~/.qaas/recordings</c>, or another folder for the recorder's tests.</summary>
    internal static string Folder
    {
        get => _folder ??= Path.Combine(Path.GetDirectoryName(BrowserDefaults.AuthStatePath)!, "recordings");
        set => _folder = value;
    }

    /// <summary>A flow file's text, by its hash, whatever its line ends.</summary>
    public static string Hash(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n"))));

    /// <summary>Writes the recording each moment it changed, of the flow as the request names it then, until stopped.</summary>
    public async Task WatchAsync(Func<Recording> recording, Func<RecordRequest> request, CancellationToken stop)
    {
        try
        {
            while (true)
            {
                await Task.Delay(Interval, stop);
                await WriteAsync(recording(), request());
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // The recording ended.
        }
    }

    /// <summary>
    /// Writes the recording as not saved yet, unless it is as last written. One of no action is not kept: the copy this
    /// recording made goes, e.g. once the person deleted every action, so none is offered when the recorder starts next.
    /// </summary>
    public async Task WriteAsync(Recording recording, RecordRequest request)
    {
        if (recording.Actions.Count == 0)
        {
            if (_path is not null && _created)
            {
                Forget();
                _written = null;
            }

            return;
        }

        var path = PathOf(request.OutputDir, request.ClassName, saved: false);
        try
        {
            var text = Stored(recording, request).ToJson();
            if (text == _written) return;
            if (path != _path)
            {
                // The flow was renamed in the panel.
                Forget();
                _created = !File.Exists(path);
            }

            await WriteFileAsync(path, text);
            (_written, _path) = (text, path);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            if (!_warned) ConsoleUi.Warning($"The recording is not saved as you go in {Folder} ({failure.Message}), so a crash " +
                                            "would lose it. Recording goes on; free some disk space, or check that you can " +
                                            "write to that folder.");
            _warned = true;
        }
    }

    /// <summary>
    /// The flow was saved, <paramref name="code"/> in <paramref name="saved"/>: its recording is kept, to continue it,
    /// unless it was saved beside a flow that was there, to merge by hand. The copy not saved yet goes.
    /// </summary>
    public async Task SavedAsync(Recording recording, RecordRequest request, SavedFlow saved, string code)
    {
        try
        {
            if (saved.CodePath.EndsWith(".cs", StringComparison.Ordinal))
            {
                await WriteFileAsync(PathOf(request.OutputDir, request.ClassName, saved: true),
                    Stored(recording, request with { Replaces = Hash(code) }).ToJson());
                Prune();
            }

            foreach (var path in new[] { _path, PathOf(request.OutputDir, request.ClassName, saved: false) }.OfType<string>())
                File.Delete(path);
            _path = null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            ConsoleUi.Warning($"The recording was not kept in {Folder} ({failure.Message}), so {request.ClassName} cannot be " +
                              "continued with --continue later; the flow itself is saved. Check that you can write to that folder.");
        }
    }

    /// <summary>
    /// Removes the copy not saved yet that this recording made, e.g. the person stopped, or nothing was recorded; one
    /// that was there before, a recording continued, stays.
    /// </summary>
    public void Forget()
    {
        try
        {
            if (_path is not null && _created) File.Delete(_path);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // It is offered when the recorder starts next, to discard then.
        }

        _path = null;
    }

    /// <summary>
    /// The recording to continue <paramref name="flow"/> of <paramref name="folder"/> from: one not saved yet, else the
    /// newer of the one kept here and the one <c>--keep-recording</c> saved beside the flow. Null when there is none.
    /// </summary>
    public static KeptRecording? Find(string folder, string flow) =>
        Read(PathOf(folder, flow, saved: false))
        ?? new[] { Read(PathOf(folder, flow, saved: true)), Read(Path.Combine(folder, $"{flow}.recording.json")) }
            .OfType<KeptRecording>().MaxBy(kept => kept.Changed);

    /// <summary>The recordings that did not end in a save (the recorder crashed, or was stopped), newest first.</summary>
    public static IReadOnlyList<KeptRecording> NotSaved()
    {
        try
        {
            var files = Directory.Exists(Folder) ? new DirectoryInfo(Folder).GetFiles("*" + Unsaved) : [];
            return [.. files.OrderByDescending(file => file.LastWriteTimeUtc).Select(file => Read(file.FullName))
                .Where(kept => kept?.Recording.Autosave is not null).OfType<KeptRecording>()];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            ConsoleUi.Warning($"Could not look for recordings that were never saved, in {Folder} ({failure.Message}). The " +
                              "recorder goes on; check that you can read that folder.");
            return [];
        }
    }

    /// <summary>
    /// The settings of the values typed that <paramref name="recording"/> does not hold, as a copy kept here holds no
    /// secret. Each is taken from <paramref name="yaml"/>, the flow's values as saved, when it is there; the others are
    /// returned.
    /// </summary>
    public static IReadOnlyList<string> Unkept(Recording recording, string flow, string? yaml = null)
    {
        var unkept = FlowPlanner.Plan(recording, flow).Steps.SelectMany(step => step.Actions)
            .Where(action => action.Kind == "fill" && action.Source.Value is null && action.Value is { Items: null })
            .ToList();
        if (unkept.Count > 0 && File.Exists(yaml))
        {
            try
            {
                var saved = LoginSettings.Read(yaml, flow);
                foreach (var action in unkept)
                    if (saved[$"{flow}:{action.Value!.Name}"] is { } value)
                        (action.Source.Value, action.Source.Raw) = (value, value);
            }
            catch (InvalidOperationException)
            {
                // Not the flow's values: they stay unknown.
            }
        }

        return [.. unkept.Where(action => action.Source.Value is null).Select(action => action.Value!.Name).Distinct()];
    }

    // The recording as kept: of the flow the request names, without the values the recorder hides.
    private static Recording Stored(Recording recording, RecordRequest request) =>
        WithoutSecrets(recording) with
        {
            Autosave = new AutosavedFlow(request.ClassName, Path.GetFullPath(request.OutputDir), request.AutoChecks,
                request.Login, request.Replaces, request.KeepRecording),
        };

    /// <summary>
    /// A copy of the recording without the values the recorder hides, a password and what is typed into a secret's field,
    /// for any file it writes: its own copy, and the raw recording <c>--keep-recording</c> keeps beside the flow.
    /// </summary>
    internal static Recording WithoutSecrets(Recording recording)
    {
        var copy = Recording.FromJson(recording.ToJson());
        foreach (var action in copy.Actions.Where(HoldsSecret))
        {
            (action.Value, action.Raw) = (null, null);
            if (action.Edit?.Values?.GetValueOrDefault("value") is { } edit) edit.Value = null;
        }

        return copy;
    }

    private static bool HoldsSecret(CapturedAction action) =>
        action.Kind == "fill"
        && (action.Element?.Type == "password" || Secrets.IsSecretKey(FlowPlanner.SettingName(action.Element))
            || action.Edit?.Values?.GetValueOrDefault("value")?.Name is { } named && Secrets.IsSecretKey(named));

    private static string PathOf(string folder, string flow, bool saved)
    {
        var whole = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{whole}\n{flow}")))[..12];
        return Path.Combine(Folder, $"{flow}-{key}{(saved ? ".json" : Unsaved)}");
    }

    // The folder too is its owner's alone; the file is written whole or not at all.
    private static Task WriteFileAsync(string path, string text)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(Folder);
        else Directory.CreateDirectory(Folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return AtomicFileWriter.WriteAsync(path, text);
    }

    // The newest saved recordings are kept; one not saved yet stays until the person said what to do with it.
    private static void Prune()
    {
        foreach (var old in new DirectoryInfo(Folder).GetFiles("*.json").Where(file => !file.Name.EndsWith(Unsaved, StringComparison.Ordinal))
                     .OrderByDescending(file => file.LastWriteTimeUtc).Skip(SavedKept))
            old.Delete();
    }

    private static KeptRecording? Read(string path)
    {
        try
        {
            return File.Exists(path) ? new KeptRecording(Recording.FromJson(File.ReadAllText(path)), File.GetLastWriteTime(path), path) : null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            ConsoleUi.Warning($"Skipped {path}: " +
                              (failure is JsonException or NotSupportedException
                                  ? "it is not a recording the recorder can read, e.g. one of an older version. Delete it if you do not need it."
                                  : $"{failure.Message}. Check that you can read it, or delete it."));
            return null;
        }
    }
}
