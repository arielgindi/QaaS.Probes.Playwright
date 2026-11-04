using System.Text.Json;
using System.Text.Json.Serialization;

namespace QaaS.Playwright.Recorder.Capture;

/// <summary>
/// What a person typed into a password's or another secret's field, or checked it holds. It is no text: printed it is
/// masked, it converts to no string, two are never compared by what they hold, and no JSON holds it, as writing one
/// throws. Only the panel's test run reads it, through <see cref="Reveal"/>, to type it as the person did; every file,
/// the console and the panel get <see cref="Masked"/>, or nothing.
/// </summary>
[JsonConverter(typeof(NeverWritten))]
internal sealed class Secret
{
    /// <summary>What a secret shows wherever it is shown.</summary>
    public const string Masked = "(hidden)";

    private readonly string _value;

    private Secret(string value) => _value = value;

    public static Secret Of(string value) => new(value);

    /// <summary>The value itself, for the one place that types it: a test run's values, never a file kept.</summary>
    public string Reveal() => _value;

    public override string ToString() => Masked;

    private sealed class NeverWritten : JsonConverter<Secret>
    {
        public override Secret Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new JsonException("A secret is never read from JSON.");

        public override void Write(Utf8JsonWriter writer, Secret value, JsonSerializerOptions options) =>
            throw new InvalidOperationException("A secret is never written to JSON: keep it out of what is serialised.");
    }
}

/// <summary>
/// Which actions hold a secret, decided from what was captured alone, never from a name the planner makes: a fill of a
/// password, or of a field whose name, id, test id or label says it holds one (a token, an API key), or that the person
/// named so in the panel; and a check of what such a field holds.
/// </summary>
internal static class SecretField
{
    private static readonly string[] Checks = ["value", "text", "contains"];

    public static bool Holds(CapturedAction action) =>
        (action.Kind == "fill" || action.Kind == "expect" && Checks.Contains(action.Check)) && Is(action.Element)
        || action.Kind == "fill" && action.Edit?.Values?.GetValueOrDefault("value")?.Name is { } named && Configuration.Secrets.IsSecretKey(named);

    /// <summary>A password, or a field that names a secret in its name, id, test id or label.</summary>
    public static bool Is(CapturedTarget? field) =>
        field is not null && (field.Type == "password" || new[] { field.Name, field.Id, field.TestId, field.Label }
            .Any(said => said is not null && Configuration.Secrets.IsSecretKey(string.Concat(said.Where(char.IsAsciiLetterOrDigit)))));
}
