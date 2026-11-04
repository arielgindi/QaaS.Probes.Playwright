using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace QaaS.Playwright.Tests.EndToEnd;

/// <summary>
/// A tiny web app on a free local port: a cookie login, a whoami page, an order page, a ticket form, a helpdesk with
/// a ticket table and a dialog to record in, orders with a menu in a portal, a to-do list, under <c>/pickers/</c>
/// the option pickers of common UI libraries, each in a modal flow (<c>Recorder/Pickers/kit.js</c>), and imitations of
/// public practice sites (<c>Recorder/Sites</c>): demoqa's practice form, and OrangeHRM's, react-admin's, Ant Design Pro's
/// and Kendo UI's demos, each under addresses of its own.
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

    /// <summary>
    /// How often a page under <c>/gate/</c> loaded without its token in localStorage, and was sent to the blocked page,
    /// as a proxy sends a first load that lacks its token into a sign-in loop.
    /// </summary>
    public int Blocked => _blocked;

    private int _blocked;

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

        if (path == "/gate" || path.StartsWith("/gate/", StringComparison.Ordinal))
        {
            ServeGate(request, response, path);
            return;
        }

        if (path.StartsWith("/pickers/", StringComparison.Ordinal))
        {
            ServePicker(response, path["/pickers/".Length..]);
            return;
        }

        // Imitations of public practice sites (Recorder/Sites): demoqa's practice form, and OrangeHRM's demo, whose every
        // page is the same app, as its addresses are.
        if (path.StartsWith("/sites/", StringComparison.Ordinal) && path.EndsWith(".js", StringComparison.Ordinal))
        {
            Send(response, "text/javascript; charset=utf-8", File.ReadAllBytes(Path.Combine(SitesFolder(), Path.GetFileName(path))));
            return;
        }

        if (path == "/automation-practice-form" || path.StartsWith("/web/index.php/", StringComparison.Ordinal)
            || SiteOf(path) is not null)
        {
            var app = SiteOf(path) ?? (path == "/automation-practice-form" ? "practice-form" : "hrm");
            var title = app switch { "hrm" => "OrangeHRM", "react-admin" => "Posters Galore", "antd-pro" => "Ant Design Pro", "kendo" => "Kendo UI", _ => "demosite" };
            var shell = $"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>{title}</title></head><body>" +
                        $"<div id=\"root\"></div><script src=\"/sites/{app}.js\"></script></body></html>";
            Send(response, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(shell));
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
            "/todos" => Todos,
            _ => "<h1>Home</h1>",
        };
        Send(response, "text/html; charset=utf-8", Encoding.UTF8.GetBytes($"<!doctype html><html><body>{body}</body></html>"));
    }

    // An app behind a gate: a page that loads without the token its login plants in localStorage goes to a page that
    // never lets it in, as a proxy's sign-in loop. Its login form, its home and a deep page of an order.
    private void ServeGate(HttpListenerRequest request, HttpListenerResponse response, string path)
    {
        if (path == "/gate/session")
        {
            Interlocked.Increment(ref _logins);
            response.AppendCookie(new Cookie("gate-user", request.QueryString["user"]) { Path = "/gate" });
            response.Redirect("/gate/home");
            response.Close();
            return;
        }

        if (path == "/gate/blocked") Interlocked.Increment(ref _blocked);
        var user = WebUtility.HtmlEncode(request.Cookies["gate-user"]?.Value ?? "nobody");
        var body = path switch
        {
            "/gate/blocked" => "<h1>Blocked</h1><script>setTimeout(() => location.replace('/gate/blocked'), 500);</script>",
            "/gate/login" => """<form action="/gate/session"><label>Username <input name="user"></label><button>Log in</button></form>""",
            "/gate/home" or "/gate" => $"""<h1>Home</h1><p id="user">{user}</p><a href="/gate/orders/42">Order 42</a>""",
            _ => $"""<h1>Order 42</h1><p id="user">{user}</p><a href="/gate/home">Home</a><button onclick="done.textContent = 'shipped'">Ship</button><p id="done"></p>""",
        };
        const string gate = "<script>if (localStorage.getItem('gate') !== 'open' && location.pathname !== '/gate/blocked') location.replace('/gate/blocked');</script>";
        Send(response, "text/html; charset=utf-8", Encoding.UTF8.GetBytes($"<!doctype html><html><head>{gate}</head><body>{body}</body></html>"));
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

    // A list that takes two tasks, then no more: its field goes away. A page can raise the limit (window.limit).
    private const string Todos = """
        <h1>Tasks</h1>
        <input id="new-task" aria-label="New task">
        <ul id="tasks"></ul>
        <script>
          window.limit = 2;
          const input = document.getElementById('new-task');
          input.addEventListener('keydown', (event) => {
            if (event.key !== 'Enter' || !input.value) return;
            tasks.appendChild(document.createElement('li')).textContent = input.value;
            input.value = '';
            if (tasks.children.length >= window.limit) input.remove();
          });
        </script>
        """;

    // react-admin's demo, Ant Design Pro's and Kendo UI's, each under addresses of its own.
    private static string? SiteOf(string path) =>
        path.StartsWith("/react-admin-demo/", StringComparison.Ordinal) ? "react-admin"
        : path.StartsWith("/antd-pro/", StringComparison.Ordinal) ? "antd-pro"
        : path.StartsWith("/kendo-ui/", StringComparison.Ordinal) ? "kendo"
        : null;

    // The kit's script, or a page that starts one of its cases.
    private static void ServePicker(HttpListenerResponse response, string name)
    {
        if (name == "kit.js")
        {
            Send(response, "text/javascript; charset=utf-8", File.ReadAllBytes(Path.Combine(PickersFolder(), "kit.js")));
            return;
        }

        var page = "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Orders</title></head><body>" +
                   $"<div id=\"root\"></div><script src=\"/pickers/kit.js\"></script><script>kit.start({JsonSerializer.Serialize(name)})</script></body></html>";
        Send(response, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(page));
    }

    private static string PickersFolder([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "..", "Recorder", "Pickers");

    private static string SitesFolder([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "..", "Recorder", "Sites");

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
