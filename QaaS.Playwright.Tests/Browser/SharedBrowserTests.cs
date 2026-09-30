using System.Net;
using System.Net.Sockets;
using QaaS.Playwright.Browser;
using QaaS.Playwright.Configuration;
using QaaS.Playwright.Tests.EndToEnd;

namespace QaaS.Playwright.Tests.Browser;

[TestFixture]
[Category("EndToEnd")]
public class SharedBrowserTests
{
    [Test]
    public async Task ABrowserThatDoesNotAnswer_DoesNotHoldUpOneAlreadyConnected()
    {
        using var chrome = await HeadlessChrome.StartAsync(DesktopPointer.LaunchFlags);
        var healthy = new PlaywrightFlowConfig { BrowserUrl = chrome.Url };
        var browser = await SharedBrowser.GetAsync(healthy, new ListLogger());

        // Takes the connection and never answers the WebSocket handshake, like a hung browser pod.
        var silent = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        var hung = new PlaywrightFlowConfig { BrowserUrl = $"ws://127.0.0.1:{((IPEndPoint)silent.LocalEndpoint).Port}/" };
        var connecting = SharedBrowser.GetAsync(hung, new ListLogger());
        using var handshake = await silent.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            Assert.That(await SharedBrowser.GetAsync(healthy, new ListLogger()).WaitAsync(TimeSpan.FromSeconds(2)),
                Is.SameAs(browser));
        }
        finally
        {
            // Refused from now on, so the hung connection gives up after its retries.
            silent.Stop();
            handshake.Close();
            Assert.ThrowsAsync<InvalidOperationException>(() => connecting);
        }
    }
}
