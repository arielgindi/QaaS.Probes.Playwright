using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace QaaS.Playwright.Tests.EndToEnd;

/// <summary>
/// A tiny web app on a free local port: a cookie login, a whoami page, a page with a test-id button, a page that
/// reports the pointer the browser has, a page with an image and a script the browser may cache, a form that
/// creates a mission in a fixed time, and an error page.
/// </summary>
public sealed class TestSite : IDisposable
{
    private static readonly Dictionary<string, (string ContentType, byte[] Body)> CacheableFiles = new()
    {
        ["/logo.png"] = ("image/png", Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==")),
        ["/app.js"] = ("text/javascript", "window.app = 1;"u8.ToArray()),
    };

    private readonly HttpListener _listener;
    private int _scriptDownloads;
    private int _logins;

    private TestSite(HttpListener listener, string url)
    {
        _listener = listener;
        Url = url;
        _ = Task.Run(ServeAsync);
    }

    public string Url { get; }

    /// <summary>How often /app.js was downloaded; the browser may cache it for an hour.</summary>
    public int ScriptDownloads => _scriptDownloads;

    /// <summary>How often someone logged in.</summary>
    public int Logins => _logins;

    /// <summary>The missions created so far. Creating one takes <see cref="MissionCreationTime"/>.</summary>
    public ConcurrentQueue<string> Missions { get; } = new();

    public static TimeSpan MissionCreationTime { get; } = TimeSpan.FromMilliseconds(250);

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
        if (CacheableFiles.TryGetValue(path, out var file))
        {
            if (path == "/app.js") Interlocked.Increment(ref _scriptDownloads);
            response.Headers["Cache-Control"] = "max-age=3600";
            Send(response, file.ContentType, file.Body);
            return;
        }

        if (path == "/error")
        {
            response.StatusCode = 500;
            Send(response, "text/html; charset=utf-8", "<h1>Internal Server Error</h1>"u8.ToArray());
            return;
        }

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
            "/pointer" => """
                <p id="pointer"></p>
                <script>pointer.textContent = ['fine', 'coarse', 'none'].find(type => matchMedia(`(pointer: ${type})`).matches);</script>
                """,
            "/image" => """
                <img src="/logo.png" onload="image.textContent = 'loaded'" onerror="image.textContent = 'blocked'">
                <p id="image"></p><script src="/app.js"></script>
                """,
            "/missions/new" => """<form action="/missions"><label>Name <input name="name"></label><button>Create</button></form>""",
            "/missions" => $"""<p id="created">{WebUtility.HtmlEncode(CreateMission(request.QueryString["name"]))}</p>""",
            _ => "<h1>Home</h1>",
        };
        Send(response, "text/html; charset=utf-8", Encoding.UTF8.GetBytes($"<!doctype html><html><body>{body}</body></html>"));
    }

    // A mission named "bad" is rejected.
    private string CreateMission(string? name)
    {
        Thread.Sleep(MissionCreationTime);
        if (name is null or "bad") return "rejected";
        Missions.Enqueue(name);
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
