using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace QaaS.Playwright.Configuration;

/// <summary>
/// A run's secrets, hidden as <c>***</c> in everything the probe records or throws: the values of FlowConfiguration
/// keys that name a password, secret, token, API key or pwd, and the password and query values of the browser URL,
/// except a number in its query whose key names no secret, e.g. <c>timeout=30000</c>.
/// </summary>
internal sealed partial class Secrets
{
    private const string Mask = "***";
    private static readonly string[] SecretWords = ["password", "secret", "token", "apikey", "pwd"];

    // Longest first, so a secret that holds another is hidden whole.
    private readonly string[] _values;

    private Secrets(IEnumerable<string> values) =>
        _values = [.. values.Where(IsWorthHiding).Distinct().OrderByDescending(value => value.Length)];

    public static Secrets None { get; } = new([]);

    /// <summary>Whether a setting's key, e.g. <c>Credentials:Password</c>, names a secret.</summary>
    public static bool IsSecretKey(string key) =>
        SecretWords.Any(word => key.Contains(word, StringComparison.OrdinalIgnoreCase));

    /// <param name="flowConfiguration">The probe's FlowConfiguration section.</param>
    /// <param name="browserUrl">The URL of the browser the run connects to.</param>
    public static Secrets Of(IConfiguration flowConfiguration, string browserUrl) =>
        new([.. SecretSettingsOf(flowConfiguration), .. CredentialsIn(browserUrl)]);

    public static Secrets InUrl(string url) => new(CredentialsIn(url));

    public string Scrub(string text) =>
        _values.Aggregate(text, (scrubbed, secret) => scrubbed.Replace(secret, Mask, StringComparison.Ordinal));

    /// <summary>
    /// The failure as it is when it holds no secret, or else one that says the same without them, with the original's
    /// type name in its message and its whole text, causes and stack included, as its ToString().
    /// </summary>
    public Exception Scrub(Exception failure)
    {
        var text = failure.ToString();
        return Scrub(text) == text
            ? failure
            : new ScrubbedException($"{failure.GetType().Name}: {Scrub(failure.Message)}", Scrub(text));
    }

    /// <summary>
    /// Throws what <see cref="Scrub(Exception)"/> returns: the failure itself, with its stack, when it holds no secret.
    /// </summary>
    [DoesNotReturn]
    public void Throw(Exception failure)
    {
        var scrubbed = Scrub(failure);
        if (scrubbed == failure) ExceptionDispatchInfo.Throw(failure);
        throw scrubbed;
    }

    // Below each flow's own section, so a flow named e.g. PasswordResetFlow does not make all its settings secret.
    private static IEnumerable<string> SecretSettingsOf(IConfiguration flowConfiguration) =>
        from flow in flowConfiguration.GetChildren()
        from setting in flow.AsEnumerable(makePathsRelative: true)
        where setting.Value is not null && IsSecretKey(setting.Key)
        select setting.Value;

    private static IEnumerable<string> CredentialsIn(string url) =>
        from match in UrlCredentials().Matches(url)
        let password = match.Groups["password"]
        let value = password.Success ? password.Value : match.Groups["value"].Value
        where password.Success || IsSecretQueryValue(Uri.UnescapeDataString(match.Groups["key"].Value), value)
        from form in new[] { value, Uri.UnescapeDataString(value) }
        select form;

    // Shorter values, and true or false, would hide ordinary text, and are no real secret.
    private static bool IsWorthHiding(string value) => value.Length >= 4 && !bool.TryParse(value, out _);

    // A number, such as a timeout, a port or an id, would hide ordinary text too, unless its key names a secret.
    private static bool IsSecretQueryValue(string key, string value) =>
        !value.All(char.IsAsciiDigit) || IsSecretKey(key);

    // The password in "ws://user:password@host", and each key and value of the query string, e.g. a Browserless token.
    [GeneratedRegex("://[^/?#@:]*:(?<password>[^/?#@]+)@|[?&](?<key>[^=&#]*)=(?<value>[^&#]+)")]
    private static partial Regex UrlCredentials();

    private sealed class ScrubbedException(string message, string text) : Exception(message)
    {
        public override string ToString() => text;
    }
}
