using System.Text.RegularExpressions;

namespace QaaS.Playwright.Browser;

/// <summary>Small helpers for the CDP URL of the browser the probe connects to.</summary>
internal static partial class BrowserUrl
{
    /// <summary>Hides the query string, which for Browserless endpoints carries the auth token, so a URL can be logged.</summary>
    public static string Redact(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Query)
            ? uri.GetLeftPart(UriPartial.Path) + "?<redacted>"
            : url;

    /// <summary>
    /// True for an http(s) URL on this machine (localhost, 127.0.0.1, ::1): a Chrome the probe may start itself.
    /// </summary>
    public static bool IsOnThisMachine(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.IsLoopback;

    /// <summary>
    /// Fails fast when the URL still holds a <c>&lt;your-namespace&gt;</c>-style placeholder, instead of a DNS
    /// error after the connect timeout.
    /// </summary>
    public static void EnsureNoTemplatePlaceholder(string url)
    {
        if (TemplatePlaceholder().IsMatch(url))
            throw new InvalidOperationException(
                $"Browser URL contains an unresolved placeholder: '{url}'. Replace the <...> tokens in " +
                "browser-defaults.yaml, or set ProbeConfiguration.BrowserUrl in YAML.");
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TemplatePlaceholder();
}
