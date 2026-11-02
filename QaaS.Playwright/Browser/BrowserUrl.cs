using System.Text.RegularExpressions;

namespace QaaS.Playwright.Browser;

/// <summary>Checks on the CDP URL of the browser the probe connects to.</summary>
internal static partial class BrowserUrl
{
    /// <summary>
    /// Hides the user-info and the query string, which carry credentials such as a Browserless token, so the URL can be
    /// logged. Works on a URL that does not parse too, e.g. one with a placeholder left in it.
    /// </summary>
    public static string Redact(string url) =>
        QueryString().Replace(UserInfo().Replace(url, "${scheme}<redacted>@"), "?<redacted>");

    /// <summary>Whether it is an http(s) URL on this machine, where the probe may start Chrome itself.</summary>
    public static bool IsOnThisMachine(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.IsLoopback;

    /// <summary>Fails fast on a leftover <c>&lt;...&gt;</c> placeholder, instead of a DNS error after a timeout.</summary>
    public static void EnsureNoTemplatePlaceholder(string url)
    {
        if (TemplatePlaceholder().IsMatch(url))
            throw new InvalidOperationException(
                $"The browser URL '{Redact(url)}' still holds a <...> placeholder. Set ProbeConfiguration.BrowserUrl " +
                "to the real address.");
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TemplatePlaceholder();

    [GeneratedRegex("^(?<scheme>[a-z][a-z0-9+.-]*://)[^/?#]*@", RegexOptions.IgnoreCase)]
    private static partial Regex UserInfo();

    [GeneratedRegex(@"\?.*", RegexOptions.Singleline)]
    private static partial Regex QueryString();
}
