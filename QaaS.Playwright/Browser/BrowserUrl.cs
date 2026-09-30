using System.Text.RegularExpressions;

namespace QaaS.Playwright.Browser;

/// <summary>Checks on the CDP URL of the browser the probe connects to.</summary>
internal static partial class BrowserUrl
{
    /// <summary>Hides the query string, which carries a Browserless token, so the URL can be logged.</summary>
    public static string Redact(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Query)
            ? uri.GetLeftPart(UriPartial.Path) + "?<redacted>"
            : url;

    /// <summary>Whether it is an http(s) URL on this machine, where the probe may start Chrome itself.</summary>
    public static bool IsOnThisMachine(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.IsLoopback;

    /// <summary>Fails fast on a leftover <c>&lt;your-namespace&gt;</c>, instead of a DNS error after a timeout.</summary>
    public static void EnsureNoTemplatePlaceholder(string url)
    {
        if (TemplatePlaceholder().IsMatch(url))
            throw new InvalidOperationException(
                $"The browser URL '{url}' still holds a <...> placeholder. Set ProbeConfiguration.BrowserUrl, " +
                "or fill in browser-defaults.yaml.");
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TemplatePlaceholder();
}
