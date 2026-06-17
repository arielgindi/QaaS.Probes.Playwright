using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using QaaS.Playwright.Configuration;

namespace QaaS.Playwright.Engine;

/// <summary>
/// Connects to the right Chrome over CDP for a probe run — a local Chrome (auto-launched when needed) or the
/// cluster/remote Chromium — applying connection retries, slow-mo, and token-safe logging. This keeps all of the
/// CDP/retry/redaction detail out of the probe, next to the rest of the browser plumbing.
/// </summary>
internal sealed partial class PlaywrightBrowserConnector(ILogger logger)
{
    /// <summary>Slow-mo applied between actions in visible mode when none is configured, so a human can watch.</summary>
    private const int DefaultVisibleSlowMoMs = 2_000;

    private const int MaxConnectAttempts = 3;

    /// <summary>Connects to the browser selected by the configuration and current environment.</summary>
    public async Task<IBrowser> ConnectAsync(IPlaywright playwright, PlaywrightFlowConfig config)
    {
        var slowMo = EffectiveSlowMo(config);

        // Visible mode (Headless=false) only makes sense locally — cluster Chrome runs headless in a container and
        // cannot show a window — so it forces local mode.
        var isLocal = !config.Headless || BrowserModeResolver.FromEnvironment() == BrowserMode.Local;

        if (isLocal)
        {
            var localUrl = string.IsNullOrWhiteSpace(config.LocalBrowserUrl)
                ? BrowserDefaults.LocalUrl
                : config.LocalBrowserUrl;
            await LocalChromeLauncher.EnsureRunningAsync(
                localUrl, config.BrowserExecutablePath, BrowserDefaults.LocalStartupTimeout, logger);
            return await AttachAsync(playwright, localUrl, slowMo, "Local");
        }

        var clusterUrl = string.IsNullOrWhiteSpace(config.RemoteBrowserUrl)
            ? BrowserDefaults.RemoteUrl
            : config.RemoteBrowserUrl;
        EnsureNoTemplatePlaceholder(clusterUrl);
        return await AttachAsync(playwright, clusterUrl, slowMo, "Cluster");
    }

    /// <summary>
    /// Slow-mo (ms) Playwright waits between every action. An explicit configured value wins (including 0 to
    /// disable it); otherwise it defaults to <see cref="DefaultVisibleSlowMoMs"/> in visible mode and 0 headless.
    /// </summary>
    private static int EffectiveSlowMo(PlaywrightFlowConfig config) =>
        config.SlowMo ?? (config.Headless ? 0 : DefaultVisibleSlowMoMs);

    private async Task<IBrowser> AttachAsync(IPlaywright playwright, string url, int slowMo, string mode)
    {
        logger.LogInformation("{Mode} mode → {Url}", mode, RedactToken(url));
        var options = new BrowserTypeConnectOverCDPOptions { SlowMo = slowMo > 0 ? slowMo : null };

        // Retry with linear backoff — a Browserless rolling restart or a brief network blip would otherwise fail
        // the run on a transient.
        Exception? lastFailure = null;
        for (var attempt = 1; attempt <= MaxConnectAttempts; attempt++)
        {
            try
            {
                return await playwright.Chromium.ConnectOverCDPAsync(url, options);
            }
            catch (Exception connectFailure)
            {
                lastFailure = connectFailure;
                if (attempt == MaxConnectAttempts) break;
                logger.LogWarning("CDP connect attempt {Attempt}/{Max} failed: {Message}",
                    attempt, MaxConnectAttempts, connectFailure.Message);
                await Task.Delay(500 * attempt);
            }
        }

        throw new InvalidOperationException(
            $"Failed to connect to {mode.ToLowerInvariant()} Chrome at {RedactToken(url)} after " +
            $"{MaxConnectAttempts} attempts. {lastFailure?.Message}", lastFailure);
    }

    /// <summary>
    /// Fails fast on the common forget-to-edit case where the configured remote URL still contains a
    /// <c>&lt;your-namespace&gt;</c>-style placeholder, which would otherwise surface as a DNS/connection error
    /// only after the connect timeout.
    /// </summary>
    private static void EnsureNoTemplatePlaceholder(string url)
    {
        if (TemplatePlaceholder().IsMatch(url))
            throw new InvalidOperationException(
                $"Browser URL contains an unresolved placeholder: '{url}'. Edit browser-defaults.yaml and replace " +
                "the <...> tokens with your real values, or set ProbeConfiguration.RemoteBrowserUrl in YAML.");
    }

    /// <summary>Redacts any query string (which may carry an auth token) before a URL is logged.</summary>
    private static string RedactToken(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Query)
            ? uri.GetLeftPart(UriPartial.Path) + "?<redacted>"
            : url;

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TemplatePlaceholder();
}
