using System.Diagnostics;

namespace QaaS.Playwright.Recorder.Capture;

/// <summary>How a login flow is going, as the console, the overlay and the panel show it while it runs.</summary>
/// <param name="Flow">The login flow.</param>
/// <param name="Seconds">How long it has run.</param>
/// <param name="Where">The page it is on, without its query: its path on the app, else its host and path.</param>
/// <param name="Last">The last thing it did on the page, e.g. <c>clicked button “Sign in”</c>; null before.</param>
internal sealed record LoginProgress(string Flow, int Seconds, string? Where, string? Last)
{
    /// <summary>It has run long enough that the person is told how to log in by hand instead.</summary>
    public bool Slow => Seconds >= LoginWatch.SlowSeconds;

    /// <summary>"Logging in with LoginFlow… 12 s, on /signin".</summary>
    public string Text => $"Logging in with {Flow}… {Seconds} s{(Where is null ? "" : $", on {Where}")}";

    /// <summary>"12 s, on /signin · clicked button “Sign in”".</summary>
    public string Detail => $"{Seconds} s{(Where is null ? "" : $", on {Where}")}{(Last is null ? "" : $" · {Last}")}";
}

/// <summary>
/// Watches a login flow from the browser, without touching the flow: how long it has run, the page it is on, the last
/// thing it did there, and what it waits for. It stops the flow, for the person to log in by hand, when they ask, when
/// it did nothing for two minutes (no page loaded, no request sent or answered), or when the app asked the browser to
/// sign in (HTTP authentication), which a flow cannot answer. Its own timeouts are the probe's, as they are.
/// </summary>
internal sealed class LoginWatch(string flow, string appUrl, Func<string, string> scrub)
{
    public const int SlowSeconds = 5;

    private static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(2);

    // A browser that signs in with Windows by itself answers the app's 401 at once.
    private static readonly TimeSpan AuthGrace = TimeSpan.FromSeconds(3);

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Lock _gate = new();
    private readonly TaskCompletionSource<string> _byHand = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<object, (string Where, TimeSpan Since)> _waiting = new(ReferenceEqualityComparer.Instance);
    private readonly Uri? _app = Uri.TryCreate(appUrl, UriKind.Absolute, out var app) ? app : null;
    private TimeSpan _active;
    private string? _url;
    private string? _last;
    private (string Why, string Url, TimeSpan At)? _auth;

    /// <summary>Completes with why the flow was stopped, for the person to log in by hand.</summary>
    public Task<string> ByHand => _byHand.Task;

    public string Flow => flow;

    /// <summary>Stops the flow, for the person to log in by hand; <paramref name="why"/> says why.</summary>
    public void Stop(string why) => _byHand.TrySetResult(why);

    /// <summary>A page of the login went to <paramref name="url"/>.</summary>
    public void Navigated(string url)
    {
        lock (_gate)
        {
            (_url, _active) = (url, _clock.Elapsed);
            if (_auth is { } auth && auth.Url != url) _auth = null;
        }
    }

    /// <summary>A request went out; a document's is waited for until it is answered.</summary>
    public void Requested(object request, string url, bool document)
    {
        lock (_gate)
        {
            _active = _clock.Elapsed;
            if (document) _waiting[request] = (Where(url), _clock.Elapsed);
        }
    }

    /// <summary>A request was answered, or failed.</summary>
    public void Answered(object request)
    {
        lock (_gate)
        {
            _active = _clock.Elapsed;
            _waiting.Remove(request);
        }
    }

    /// <summary>The flow clicked or typed on the page, e.g. <c>clicked button “Sign in”</c>.</summary>
    public void Acted(string what)
    {
        lock (_gate) _last = scrub(what);
    }

    /// <summary>A page of the login got a 401 asking the browser to sign in with <paramref name="scheme"/>.</summary>
    public void AuthAsked(string scheme, string url)
    {
        var windows = new[] { "Negotiate", "NTLM", "Kerberos" }.Any(name => scheme.StartsWith(name, StringComparison.OrdinalIgnoreCase));
        var password = new[] { "Basic", "Digest" }.Any(name => scheme.StartsWith(name, StringComparison.OrdinalIgnoreCase));
        if (!windows && !password) return;
        var why = windows
            ? "The app asked the browser to sign in with Windows, which a flow cannot answer."
            : "The app asked the browser for a user name and a password (HTTP authentication), which a flow cannot answer.";
        lock (_gate) _auth = (why, url, _clock.Elapsed);
    }

    /// <summary>How it goes now; stops the flow when it did nothing for too long, or the app asked the browser to sign in.</summary>
    public LoginProgress Tick()
    {
        lock (_gate)
        {
            var now = _clock.Elapsed;
            if (_auth is { } auth && now - auth.At >= AuthGrace) Stop(auth.Why);
            else if (now - _active >= IdleLimit) Stop(Idle());
            return new LoginProgress(flow, (int)now.TotalSeconds, _url is null ? null : Where(_url), _last);
        }
    }

    // What it was waiting for, when it did nothing for so long.
    private string Idle()
    {
        var said = $"{flow} did nothing for {(int)IdleLimit.TotalMinutes} minutes: no page loaded, and nothing went to the app or " +
                   "came back.";
        var where = _url is null ? null : Where(_url);
        if (_waiting.Values.OrderBy(waiting => waiting.Since).FirstOrDefault() is { Where: { Length: > 0 } waited })
            return $"{said} It was waiting for an answer from {waited}.";
        return _last is not null ? $"{said} The last thing it did was: {_last}{(where is null ? "" : $", on {where}")}."
            : where is not null ? $"{said} It is on {where}."
            : said;
    }

    // "/signin" on the app; "login.example.com/adfs/ls" elsewhere; never the query, which may hold a token.
    private string Where(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return scrub(url);
        var sameApp = _app is not null && uri.Scheme == _app.Scheme && uri.Authority == _app.Authority;
        return scrub(sameApp ? uri.AbsolutePath : $"{uri.Authority}{uri.AbsolutePath.TrimEnd('/')}");
    }
}
