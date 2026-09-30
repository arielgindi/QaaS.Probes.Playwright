using System.Net;
using System.Net.Sockets;
using System.Text;

namespace QaaS.Playwright.Tests.EndToEnd;

/// <summary>
/// A tiny web app on a free local port: a cookie login, a whoami page, a page with a test-id button and a page that
/// reports the pointer the browser has.
/// </summary>
public sealed class TestSite : IDisposable
{
    private readonly HttpListener _listener;

    private TestSite(HttpListener listener, string url)
    {
        _listener = listener;
        Url = url;
        _ = Task.Run(ServeAsync);
    }

    public string Url { get; }

    public static TestSite Start()
    {
        // HttpListener cannot bind port 0, so take a free port and retry if another process grabs it first.
        for (var attempt = 1; ; attempt++)
        {
            var url = $"http://127.0.0.1:{FreePort()}";
            var listener = new HttpListener { Prefixes = { url + "/" } };
            try
            {
                listener.Start();
                return new TestSite(listener, url);
            }
            catch (HttpListenerException) when (attempt < 5)
            {
                listener.Close();
            }
        }
    }

    public void Dispose() => _listener.Close();

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext request;
            try { request = await _listener.GetContextAsync(); }
            catch (Exception failure) when (failure is HttpListenerException or ObjectDisposedException) { return; }
            _ = Task.Run(() => Respond(request));
        }
    }

    private static void Respond(HttpListenerContext http)
    {
        var (request, response) = (http.Request, http.Response);
        if (request.Url!.AbsolutePath == "/session")
        {
            // The login form submits here: remember the user in a cookie, then show who is logged in.
            response.AppendCookie(new Cookie("user", request.QueryString["user"]) { Path = "/" });
            response.Redirect("/whoami");
            response.Close();
            return;
        }

        var body = request.Url.AbsolutePath switch
        {
            "/login" => """<form action="/session"><label>Username <input name="user"></label><button>Log in</button></form>""",
            "/whoami" => $"""<p id="user">{WebUtility.HtmlEncode(request.Cookies["user"]?.Value ?? "nobody")}</p>""",
            "/order" => """
                <button data-testid="submit-order" onclick="result.textContent = 'submitted'">Submit</button>
                <p id="result"></p>
                """,
            "/pointer" => """
                <p id="pointer"></p>
                <script>pointer.textContent = ['fine', 'coarse', 'none'].find(type => matchMedia(`(pointer: ${type})`).matches);</script>
                """,
            _ => "<h1>Home</h1>",
        };
        var bytes = Encoding.UTF8.GetBytes($"<!doctype html><html><body>{body}</body></html>");
        response.ContentType = "text/html; charset=utf-8";
        response.OutputStream.Write(bytes);
        response.Close();
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
