using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using QaaS.Playwright.Configuration;

namespace QaaS.Playwright.Browser;

/// <summary>
/// One Playwright driver per process, and one CDP connection per browser URL and slow-mo, shared by every run: a
/// driver and a connection of its own cost each run ~170 ms, and ten parallel runs ten drivers. A connection that
/// dropped, e.g. because Chrome restarted, is replaced on the next run. The driver stops when the process exits.
/// </summary>
internal static class SharedBrowser
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Dictionary<(string Url, int SlowMo), IBrowser> Connections = [];
    private static IPlaywright? _driver;

    public static async Task<IBrowser> GetAsync(PlaywrightFlowConfig config, ILogger logger)
    {
        var key = (BrowserConnector.UrlOf(config), BrowserConnector.SlowMoOf(config));
        await Gate.WaitAsync();
        try
        {
            if (Connections.TryGetValue(key, out var browser) && browser.IsConnected) return browser;

            _driver ??= await StartDriverAsync();
            browser = await BrowserConnector.ConnectAsync(_driver, config, logger);
            Connections[key] = browser;
            return browser;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<IPlaywright> StartDriverAsync()
    {
        var driver = await Microsoft.Playwright.Playwright.CreateAsync();
        // The recorder records the same attribute, so recorded GetByTestId() calls resolve the same way here.
        driver.Selectors.SetTestIdAttribute(BrowserDefaults.TestIdAttribute);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => driver.Dispose();
        return driver;
    }
}
