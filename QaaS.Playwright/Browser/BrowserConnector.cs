using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using QaaS.Playwright.Configuration;

namespace QaaS.Playwright.Browser;

/// <summary>
/// Connects to the Chrome at <see cref="PlaywrightFlowConfig.BrowserUrl"/> over CDP, retrying brief outages.
/// When that URL is on this machine and nothing answers there, it starts Chrome first.
/// </summary>
internal sealed class BrowserConnector(ILogger logger)
{
    // With Headless=false a person is watching, so actions are slowed down unless SlowMo says otherwise.
    private const int WatchedSlowMoMs = 2_000;

    private const int MaxConnectAttempts = 3;

    public async Task<IBrowser> ConnectAsync(
        IPlaywright playwright, PlaywrightFlowConfig config, CancellationToken ct = default)
    {
        // The recorder records the same attribute, so recorded GetByTestId() calls resolve the same way here.
        playwright.Selectors.SetTestIdAttribute(BrowserDefaults.TestIdAttribute);

        var url = string.IsNullOrWhiteSpace(config.BrowserUrl) ? BrowserDefaults.BrowserUrl : config.BrowserUrl;
        BrowserUrl.EnsureNoTemplatePlaceholder(url);
        if (BrowserUrl.IsOnThisMachine(url))
            await LocalChromeLauncher.EnsureRunningAsync(
                url, config.BrowserExecutablePath, BrowserDefaults.ChromeStartupTimeout, logger, ct);

        var slowMo = config.SlowMo ?? (config.Headless ? 0 : WatchedSlowMoMs);
        return await ConnectWithRetriesAsync(playwright, url, slowMo, ct);
    }

    // A browser pool restarting or a network blip should not fail the run, so connecting is retried with backoff.
    private async Task<IBrowser> ConnectWithRetriesAsync(IPlaywright playwright, string url, int slowMo, CancellationToken ct)
    {
        logger.LogInformation("Connecting to {Url}", BrowserUrl.Redact(url));
        var options = new BrowserTypeConnectOverCDPOptions { SlowMo = slowMo > 0 ? slowMo : null };

        for (var attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await playwright.Chromium.ConnectOverCDPAsync(url, options);
            }
            catch (Exception failure) when (attempt < MaxConnectAttempts)
            {
                logger.LogWarning("CDP connect attempt {Attempt}/{Max} failed: {Message}",
                    attempt, MaxConnectAttempts, failure.Message);
                await Task.Delay(500 * attempt, ct);
            }
            catch (Exception failure)
            {
                throw new InvalidOperationException(
                    $"Failed to connect to Chrome at {BrowserUrl.Redact(url)} after {MaxConnectAttempts} attempts. " +
                    failure.Message, failure);
            }
        }
    }
}
