using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using QaaS.Playwright.Configuration;

namespace QaaS.Playwright.Browser;

/// <summary>
/// Connects to the run's Chrome over CDP, starting it first when it is on this machine and not running. With
/// <c>ENV=local</c> or <c>Headless: false</c> that is a Chrome on this machine, whatever BrowserUrl says.
/// </summary>
internal static class BrowserConnector
{
    /// <summary>The Chrome on this machine a run uses with <c>ENV=local</c> or <c>Headless: false</c>.</summary>
    public const string LocalUrl = "http://localhost:9222";

    // With Headless: false a person is watching, so actions are slowed down unless SlowMo says otherwise.
    private const int WatchedSlowMoMs = 2_000;

    private const int MaxAttempts = 3;

    /// <summary>
    /// The CDP endpoint of the run's Chrome: BrowserUrl, or the built-in default; but with <c>ENV=local</c> or
    /// <c>Headless: false</c> a Chrome on this machine: BrowserUrl when it is on this machine, else <see cref="LocalUrl"/>.
    /// </summary>
    public static string UrlOf(PlaywrightFlowConfig config) => UrlOf(config, Environment.GetEnvironmentVariable("ENV"));

    internal static string UrlOf(PlaywrightFlowConfig config, string? env)
    {
        var configured = ConfiguredUrlOf(config);
        return WantsLocalChrome(config, env) && !BrowserUrl.IsOnThisMachine(configured) ? LocalUrl : configured;
    }

    // ENV=local is how developers have always asked for their own Chrome; Headless: false means a person is watching.
    private static bool WantsLocalChrome(PlaywrightFlowConfig config, string? env) =>
        !config.Headless || string.Equals(env?.Trim(), "local", StringComparison.OrdinalIgnoreCase);

    private static string ConfiguredUrlOf(PlaywrightFlowConfig config) =>
        string.IsNullOrWhiteSpace(config.BrowserUrl) ? BrowserDefaults.BrowserUrl : config.BrowserUrl;

    public static int SlowMoOf(PlaywrightFlowConfig config) => config.SlowMo ?? (config.Headless ? 0 : WatchedSlowMoMs);

    public static async Task<IBrowser> ConnectAsync(IPlaywright playwright, PlaywrightFlowConfig config, ILogger logger)
    {
        var url = UrlOf(config);
        BrowserUrl.EnsureNoTemplatePlaceholder(url);
        if (BrowserUrl.IsOnThisMachine(url))
            await LocalChromeLauncher.EnsureRunningAsync(url, config.BrowserExecutablePath, logger);

        var slowMo = SlowMoOf(config);
        if (url != ConfiguredUrlOf(config))
            logger.LogInformation("{Reason}: using the Chrome on this machine at {Url} instead of {Configured}",
                config.Headless ? "ENV=local" : "Headless: false", url, BrowserUrl.Redact(ConfiguredUrlOf(config)));
        logger.LogInformation("Connecting to {Url}", BrowserUrl.Redact(url));
        return await ConnectWithRetriesAsync(playwright, url, new() { SlowMo = slowMo > 0 ? slowMo : null }, logger);
    }

    // A browser pool restarting or a network blip should not fail the run. Playwright's messages quote the URL, so
    // its credentials are hidden in them.
    private static async Task<IBrowser> ConnectWithRetriesAsync(
        IPlaywright playwright, string url, BrowserTypeConnectOverCDPOptions options, ILogger logger)
    {
        var secrets = Secrets.InUrl(url);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await playwright.Chromium.ConnectOverCDPAsync(url, options);
            }
            catch (Exception failure) when (attempt < MaxAttempts)
            {
                logger.LogWarning("CDP connect attempt {Attempt}/{Max} failed: {Message}",
                    attempt, MaxAttempts, secrets.Scrub(failure.Message));
                await Task.Delay(500 * attempt);
            }
            catch (Exception failure)
            {
                throw new InvalidOperationException(
                    $"Could not connect to Chrome at {BrowserUrl.Redact(url)} after {MaxAttempts} attempts: " +
                    secrets.Scrub(failure.Message));
            }
        }
    }
}
