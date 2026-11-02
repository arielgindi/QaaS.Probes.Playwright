using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using QaaS.Playwright.Configuration;

namespace QaaS.Playwright.Browser;

/// <summary>
/// One Playwright driver per process, and one CDP connection per browser URL and slow-mo, shared by every run: a
/// driver and a connection of its own cost each run ~170 ms, and ten parallel runs ten drivers. Connecting to one
/// browser never holds up runs on another. A connection that failed or dropped, e.g. because Chrome restarted, is made
/// again by the next run. The driver stops when the process exits.
/// </summary>
internal static class SharedBrowser
{
    private static readonly Lazy<Task<IPlaywright>> Driver = new(StartDriverAsync);

    // Lazy, so runs that ask for a browser at once wait for the same connection instead of each making one.
    private static readonly ConcurrentDictionary<(string Url, int SlowMo), Lazy<Task<IBrowser>>> Connections = new();

    public static async Task<IBrowser> GetAsync(PlaywrightFlowConfig config, ILogger logger)
    {
        var key = (BrowserConnector.UrlOf(config), BrowserConnector.SlowMoOf(config));
        var connection = Connections.GetOrAdd(key, _ => new(() => ConnectAsync(config, logger)));
        IBrowser? browser = null;
        try
        {
            browser = await connection.Value;
        }
        finally
        {
            if (browser is not { IsConnected: true }) Connections.TryRemove(KeyValuePair.Create(key, connection));
        }

        return browser.IsConnected ? browser : await GetAsync(config, logger);
    }

    private static async Task<IBrowser> ConnectAsync(PlaywrightFlowConfig config, ILogger logger) =>
        await BrowserConnector.ConnectAsync(await Driver.Value, config, logger);

    private static async Task<IPlaywright> StartDriverAsync()
    {
        var driver = await Microsoft.Playwright.Playwright.CreateAsync();
        // The recorder records the same attribute, so recorded GetByTestId() calls resolve the same way here.
        driver.Selectors.SetTestIdAttribute(BrowserDefaults.TestIdAttribute);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => driver.Dispose();
        return driver;
    }
}
