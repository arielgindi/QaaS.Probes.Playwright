using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace QaaS.Playwright.Tests.EndToEnd;

/// <summary>
/// A tiny web app on a free local port: a cookie login, a whoami page, an order page, a ticket form, a helpdesk with
/// a ticket table and a dialog to record in, and orders with a menu in a portal.
/// </summary>
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
            "/helpdesk" => Helpdesk,
            "/orders" => Orders,
            _ => "<h1>Home</h1>",
        };
        Send(response, "text/html; charset=utf-8", Encoding.UTF8.GetBytes($"<!doctype html><html><body>{body}</body></html>"));
    }

    // A button with a generated id, three buttons of one name told apart by their row, and a dialog whose form saves to
    // a new address with a message.
    private const string Helpdesk = """
        <h1>Helpdesk</h1>
        <button id=":r5:" data-testid="new-ticket" onclick="openForm()">New ticket</button>
        <table>
          <thead><tr><th scope="col">Ticket</th><th scope="col">Subject</th><th scope="col">Owner</th></tr></thead>
          <tbody>
            <tr><td>TCK-1040</td><td>Password reset</td><td><button onclick="assign(this)">Assign to me</button></td></tr>
            <tr><td>TCK-1041</td><td>VPN drops every hour</td><td><button onclick="assign(this)">Assign to me</button></td></tr>
            <tr><td>TCK-1042</td><td>Laptop for a new starter</td><td><button onclick="assign(this)">Assign to me</button></td></tr>
          </tbody>
        </table>
        <div role="dialog" aria-labelledby="form-title" id="form" hidden>
          <h2 id="form-title">New ticket</h2>
          <label>Title <input name="title"></label>
          <label>Category <select name="category"><option>Hardware</option><option>Software</option><option>Access</option></select></label>
          <fieldset>
            <legend>Priority</legend>
            <label><input type="radio" name="priority" value="low"> Low</label>
            <label><input type="radio" name="priority" value="normal" checked> Normal</label>
            <label><input type="radio" name="priority" value="high"> High</label>
          </fieldset>
          <button onclick="form.hidden = true">Cancel</button>
          <button onclick="create()">Create ticket</button>
        </div>
        <script>
          const say = (text) => document.body.insertAdjacentHTML('beforeend', `<p role="alert">${text}</p>`);
          const openForm = () => { form.hidden = false; };
          const assign = (button) => say(`${button.closest('tr').cells[0].textContent} assigned to you`);
          const create = () => { form.hidden = true; history.pushState({}, '', '/tickets/1043'); say('Ticket TCK-1043 created'); };
        </script>
        """;

    // A menu of a row, opened in a portal at the end of the body, which closes as soon as the focus leaves it.
    private const string Orders = """
        <h1>Orders</h1>
        <div id="app"><button aria-haspopup="menu" onclick="openMenu()">Actions</button></div>
        <script>
          function openMenu() {
            const portal = document.body.appendChild(document.createElement('div'));
            portal.className = 'popover';
            portal.innerHTML = '<div class="paper"><ul role="menu" aria-label="Order actions">' +
              '<li role="menuitem" tabindex="-1">Edit</li><li role="menuitem" tabindex="-1">Archive</li></ul></div>';
            portal.querySelector('li').focus();
            portal.addEventListener('focusout', () => portal.remove());
          }
        </script>
        """;

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
