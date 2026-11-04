using Microsoft.Playwright;
using QaaS.Playwright.Browser;

namespace QaaS.Playwright.Recorder.Capture;

/// <summary>
/// The recorder's own Chrome, launched once: the recorder's window is a page of it, and each recording a browser
/// context of it, opened as the recording starts and closed as it ends. Launched with no window of its own, as
/// Playwright launches a Chrome of no persistent profile, so the first window is the one the recorder opens.
/// </summary>
internal sealed class RecorderBrowser : IAsyncDisposable
{
    private const float LaunchTimeoutMs = 60_000;

    // A driver reads PWDEBUG from the process as it starts: sessions that start one at the same time take turns, so none
    // starts with the console API's value, which turns off every timeout, nor leaves it set for the drivers after it.
    internal static readonly SemaphoreSlim DriverStart = new(1, 1);

    private int _disposed;

    private RecorderBrowser(IPlaywright playwright, IBrowser browser) => (Playwright, Browser) = (playwright, browser);

    public IPlaywright Playwright { get; }

    public IBrowser Browser { get; }

    /// <summary>Launches the installed Chrome, with <paramref name="browserArgs"/>, e.g. <c>--headless=new</c> for tests.</summary>
    /// <exception cref="PlaywrightException">Chrome is not installed, or did not start.</exception>
    public static async Task<RecorderBrowser> LaunchAsync(IReadOnlyList<string> browserArgs)
    {
        IPlaywright playwright;
        await DriverStart.WaitAsync();
        try
        {
            playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        }
        finally
        {
            DriverStart.Release();
        }

        try
        {
            playwright.Selectors.SetTestIdAttribute(BrowserDefaults.TestIdAttribute);
            var browser = await playwright.Chromium.LaunchAsync(new()
            {
                Channel = BrowserDefaults.ChromeChannel, Headless = false, Timeout = LaunchTimeoutMs,
                // Ctrl+C in the console reaches the driver too, which would close Chrome: the recording ends on it as on
                // Save, with Chrome still open, so the login is saved as ever.
                HandleSIGINT = false,
                // Port 0: Chrome takes a free one, on this machine only, for the console API's driver of each recording.
                Args = [.. browserArgs, "--remote-debugging-port=0"],
            });
            return new RecorderBrowser(playwright, browser);
        }
        catch
        {
            playwright.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        try
        {
            await Browser.CloseAsync();
        }
        catch (PlaywrightException)
        {
            // Already closed by the person.
        }

        Playwright.Dispose();
    }
}
