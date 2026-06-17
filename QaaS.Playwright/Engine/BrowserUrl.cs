using System.Text.RegularExpressions;

namespace QaaS.Playwright.Engine;

/// <summary>Small, pure helpers for the CDP browser URLs the connector works with.</summary>
internal static partial class BrowserUrl
{
    /// <summary>
    /// Redacts any query string — which for remote/Browserless endpoints commonly carries an auth token — so a
    /// URL is safe to log.
    /// </summary>
    public static string Redact(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Query)
            ? uri.GetLeftPart(UriPartial.Path) + "?<redacted>"
            : url;

    /// <summary>
    /// Fails fast on the common forget-to-edit case where the configured remote URL still contains a
    /// <c>&lt;your-namespace&gt;</c>-style placeholder, which would otherwise surface as a DNS/connection error
    /// only after the connect timeout.
    /// </summary>
    /// <exception cref="InvalidOperationException">The URL still contains a <c>&lt;...&gt;</c> placeholder.</exception>
    public static void EnsureNoTemplatePlaceholder(string url)
    {
        if (TemplatePlaceholder().IsMatch(url))
            throw new InvalidOperationException(
                $"Browser URL contains an unresolved placeholder: '{url}'. Edit browser-defaults.yaml and replace " +
                "the <...> tokens with your real values, or set ProbeConfiguration.RemoteBrowserUrl in YAML.");
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TemplatePlaceholder();
}
