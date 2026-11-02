using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace QaaS.Playwright.Tests.EndToEnd;

/// <summary>A tiny web app on a free local port: a cookie login, a whoami page, an order page and a ticket form.</summary>
public sealed class TestSite : IDisposable
{
    private readonly HttpListener _listener;
    private int _logins;

    private TestSite(HttpListener listener, string url)
    {
        _listener = listener;
        Url = url;
        _ = Task.Run(ServeAsync);
    }

    public string Url { get; }

    /// <summary>How often someone logged in.</summary>
    public int Logins => _logins;

    /// <summary>The tickets created so far.</summary>
    public ConcurrentQueue<string> Tickets { get; } = new();

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

    private void Respond(HttpListenerContext http)
    {
        var (request, response) = (http.Request, http.Response);
        var path = request.Url!.AbsolutePath;
        if (path == "/session")
        {
            Interlocked.Increment(ref _logins);
            // The login form submits here: remember the user in a cookie, then show who is logged in.
            response.AppendCookie(new Cookie("user", request.QueryString["user"]) { Path = "/" });
            response.Redirect("/whoami");
            response.Close();
            return;
        }

        var body = path switch
        {
            "/login" => """<form action="/session"><label>Username <input name="user"></label><button>Log in</button></form>""",
            "/whoami" => $"""<p id="user">{WebUtility.HtmlEncode(request.Cookies["user"]?.Value ?? "nobody")}</p>""",
            "/order" => """
                <button data-testid="submit-order" onclick="result.textContent = 'submitted'">Submit</button>
                <p id="result"></p>
                """,
            "/tickets/new" => """<form action="/tickets"><label>Name <input name="name"></label><button>Create</button></form>""",
            "/tickets" => $"""<p id="created">{WebUtility.HtmlEncode(CreateTicket(request.QueryString["name"]))}</p>""",
            _ => "<h1>Home</h1>",
        };
        Send(response, "text/html; charset=utf-8", Encoding.UTF8.GetBytes($"<!doctype html><html><body>{body}</body></html>"));
    }

    private string CreateTicket(string? name)
    {
        if (name is null) return "rejected";
        Tickets.Enqueue(name);
        return name;
    }

    private static void Send(HttpListenerResponse response, string contentType, byte[] body)
    {
        response.ContentType = contentType;
        response.OutputStream.Write(body);
        response.Close();
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
