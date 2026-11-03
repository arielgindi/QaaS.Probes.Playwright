using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <exception cref="JsonException">The text is not a recording.</exception>
    public static Recording FromJson(string json) =>
        JsonSerializer.Deserialize<Recording>(json, Json) ?? throw new JsonException("Not a recording.");
}
