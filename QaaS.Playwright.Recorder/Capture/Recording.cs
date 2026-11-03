using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using QaaS.Playwright.Recorder.Login;

namespace QaaS.Playwright.Recorder.Capture;

/// <summary>What was recorded: the page it started on and the actions, each with what it caused.</summary>
internal sealed record Recording(string StartUrl, IReadOnlyList<CapturedAction> Actions)
{
    // Text in every script kept readable, and what is false, zero or missing left out.
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    /// <summary>What was seen but cannot be replayed, for the person to do by hand; null when nothing.</summary>
    public IReadOnlyList<string>? NotRecorded { get; init; }

    /// <summary>The flow that ran before the recording started, e.g. a login flow, which the flow must run after.</summary>
    public string? After { get; init; }

    /// <summary>
    /// Whether the flow checks after each action what it did, as the person turned it in the panel; null leaves it to
    /// the command line (<c>--no-auto-checks</c>).
    /// </summary>
    public bool? AutoChecks { get; init; }

    /// <summary>In the recorder's own copy of a recording, in <c>~/.qaas/recordings</c>: the flow it is of; else null.</summary>
    public AutosavedFlow? Autosave { get; init; }

    /// <summary>
    /// The recording starts by logging in, as a password is typed before the app went to another address: it was
    /// recorded signed out, so it is replayed, tested and continued so, never from the recorder's saved login.
    /// </summary>
    [JsonIgnore]
    public bool LogsIn
    {
        get
        {
            if (After is not null) return false;
            foreach (var action in Actions)
            {
                if (action is { Kind: "fill", Target.Type: "password" }) return true;
                if (action.Kind is "navigate" or "newTab" || action.Effects.Any(effect => effect.Kind is "urlChanged" or "popup")) return false;
            }

            return false;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <exception cref="JsonException">The text is not a recording.</exception>
    public static Recording FromJson(string json) =>
        JsonSerializer.Deserialize<Recording>(json, Json) is { StartUrl: not null, Actions: { } actions } recording
        && actions.All(action => action is not null)
            ? recording
            : throw new JsonException("Not a recording.");
}

/// <summary>The flow a recording is of, as the recorder keeps it in its own copy, to save or continue it later.</summary>
/// <param name="Flow">The flow's class name.</param>
/// <param name="Folder">The folder the flow is saved in, whole.</param>
/// <param name="AutoChecks">Whether the flow checks what each action did.</param>
/// <param name="Login">The login flow the recording ran after.</param>
/// <param name="Replaces">The flow file that saving it replaces, by its hash (<see cref="RecordRequest.Replaces"/>).</param>
/// <param name="KeepRecording">The raw recording is saved beside the flow too (<c>--keep-recording</c>).</param>
internal sealed record AutosavedFlow(string Flow, string Folder, bool AutoChecks, LoginFlow? Login, string? Replaces,
    bool KeepRecording = false);
