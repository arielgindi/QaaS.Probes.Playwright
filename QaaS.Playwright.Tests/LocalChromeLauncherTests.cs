using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using QaaS.Playwright.Engine;

namespace QaaS.Playwright.Tests;

[TestFixture]
public class LocalChromeLauncherTests
{
    [Test]
    public async Task IsReachable_NoServer_ReturnsFalse()
    {
        // Reserved unassigned port range — guaranteed nothing listening.
        Assert.That(await LocalChromeLauncher.IsReachableAsync("http://localhost:1"), Is.False);
    }

    [Test]
    public async Task IsReachable_StubServerReturns200_ReturnsTrue()
    {
        await using var server = new StubCdpServer();
        Assert.That(await LocalChromeLauncher.IsReachableAsync(server.Url), Is.True);
    }

    [Test]
    public void EnsureRunning_BadUrl_ThrowsArgumentException()
    {
        var ex = Assert.ThrowsAsync<ArgumentException>(() =>
            LocalChromeLauncher.EnsureRunningAsync(
                "not-a-url", executablePathOverride: null,
                startupTimeout: TimeSpan.FromSeconds(1), NullLogger.Instance));
        Assert.That(ex!.Message, Does.Contain("absolute"));
    }

    /// <summary>Minimal HTTP server that answers /json/version with 200 OK.</summary>
    private sealed class StubCdpServer : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        public string Url { get; }

        public StubCdpServer()
        {
            var port = FindFreePort();
            Url = $"http://localhost:{port}";
            _listener.Prefixes.Add(Url + "/");
            _listener.Start();
            _ = Task.Run(Loop);
        }

        private async Task Loop()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var ctx = await _listener.GetContextAsync();
                    ctx.Response.StatusCode = 200;
                    ctx.Response.Close();
                }
            }
            catch { /* listener stopped */ }
        }

        private static int FindFreePort()
        {
            var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            l.Start();
            var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        public ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            return ValueTask.CompletedTask;
        }
    }
}
