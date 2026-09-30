using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using QaaS.Playwright.Configuration;

namespace QaaS.Playwright.Browser;

/// <summary>Connects to the Chrome at BrowserUrl over CDP, starting it first when it is local and not running.</summary>
internal static class BrowserConnector
{
    // With Headless: false a person is watching, so actions are slowed down unless SlowMo says otherwise.
    private const int WatchedSlowMoMs = 2_000;

    private const int MaxAttempts = 3;

    public static async Task<IBrowser> ConnectAsync(IPlaywright playwright, PlaywrightFlowConfig config, ILogger logger)
    {
        // The recorder records the same attribute, so recorded GetByTestId() calls resolve the same way here.
        playwright.Selectors.SetTestIdAttribute(BrowserDefaults.TestIdAttribute);

        var url = string.IsNullOrWhiteSpace(config.BrowserUrl) ? BrowserDefaults.BrowserUrl : config.BrowserUrl;
        BrowserUrl.EnsureNoTemplatePlaceholder(url);
        if (BrowserUrl.IsOnThisMachine(url))
            await LocalChromeLauncher.EnsureRunningAsync(url, config.BrowserExecutablePath, logger);

        var slowMo = config.SlowMo ?? (config.Headless ? 0 : WatchedSlowMoMs);
        logger.LogInformation("Connecting to {Url}", BrowserUrl.Redact(url));
        return await ConnectWithRetriesAsync(playwright, url, new() { SlowMo = slowMo > 0 ? slowMo : null }, logger);
    }

    // A browser pool restarting or a network blip should not fail the run.
    private static async Task<IBrowser> ConnectWithRetriesAsync(
        IPlaywright playwright, string url, BrowserTypeConnectOverCDPOptions options, ILogger logger)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await playwright.Chromium.ConnectOverCDPAsync(url, options);
            }
            catch (Exception failure) when (attempt < MaxAttempts)
            {
                logger.LogWarning(
                    "CDP connect attempt {Attempt}/{Max} failed: {Message}", attempt, MaxAttempts, failure.Message);
                await Task.Delay(500 * attempt);
            }
            catch (Exception failure)
            {
                throw new InvalidOperationException(
                    $"Could not connect to Chrome at {BrowserUrl.Redact(url)} after {MaxAttempts} attempts: " +
                    failure.Message, failure);
            }
        }
    }
}
