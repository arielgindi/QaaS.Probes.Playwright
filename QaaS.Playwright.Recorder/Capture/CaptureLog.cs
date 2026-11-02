using System.Text.Json;
using QaaS.Playwright.Recorder.Generation;

namespace QaaS.Playwright.Recorder.Capture;

/// <summary>
/// Collects what the capture script reports from every page, and what Playwright sees itself (navigations, pop-ups,
/// browser dialogs), then ties each change of the page to the action that caused it. Safe to call from any thread.
/// </summary>
internal sealed class CaptureLog
{
    // An action causes what follows it this soon; later, the page changed by itself or the person did something else.
    private const long DialogWindowMs = 5_000;
    private const long AlertWindowMs = 2_500;
    private const long NavigationWindowMs = 5_000;

    private static readonly HashSet<string> CausesNavigation =
        ["click", "dblclick", "press", "choose", "check", "uncheck", "drag"];

    private readonly Lock _gate = new();
    private readonly List<CapturedAction> _actions = [];
    private readonly Dictionary<string, int> _indexById = [];
    private readonly List<CapturedEffect> _effects = [];
    private readonly List<PageEvent> _pageEvents = [];
    private readonly List<NavigationRequest> _navigationRequests = [];
    private readonly HashSet<string> _removed = [];
    private readonly Dictionary<string, int?> _chosen = [];
    private long _sequence;
    private long _version;

    /// <summary>Changes whenever what <see cref="Build"/> gives may have changed, so a view of the recording refreshes.</summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>
    /// Takes a message of the capture script. Returns the action to show the person, once it is complete, or the
    /// effect; null for a message that adds nothing to show (a fill still being typed, an unknown message).
    /// </summary>
    public object? Add(string page, string json)
    {
        Message? message;
        try
        {
            message = JsonSerializer.Deserialize<Message>(json, Recording.Json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (message is null) return null;
        lock (_gate)
        {
            // Inside the lock, so a Build that saw this version waits for the change.
            Changed();
            if (message.Type == "effect")
            {
                var effect = message.ToEffect();
                _effects.Add(effect);
                return effect;
            }

            if (message.Type != "action" || message.Id is null) return null;
            var action = message.ToAction(page);
            if (_indexById.TryGetValue(action.Id, out var index))
            {
                var previous = _actions[index];
                _actions[index] = action;
                // A fill is shown once it is final; a click becomes a double-click.
                return previous.Final == false && action.Final != false || previous.Kind != action.Kind ? action : null;
            }

            _indexById[action.Id] = _actions.Count;
            _actions.Add(action);
            return action.Final == false ? null : action;
        }
    }

    /// <summary>A page went to another address, by a link, a script or the address bar.</summary>
    public void Navigated(string page, string url, long at)
    {
        lock (_gate) _pageEvents.Add(new PageEvent("navigated", page, url, at, _sequence++));
        Changed();
    }

    /// <summary>
    /// A page asked for a new document. <paramref name="byThePerson"/> tells, once the request's headers are known,
    /// whether the person started it (the address bar, a bookmark) rather than a link, a form or the app's script.
    /// </summary>
    public void NavigationRequested(string page, Task<bool> byThePerson)
    {
        lock (_gate) _navigationRequests.Add(new NavigationRequest(page, _sequence++, byThePerson));
        Changed();
        // Who started it tells whether it is an action of its own, once it is known.
        _ = byThePerson.ContinueWith(_ => Changed(), TaskScheduler.Default);
    }

    /// <summary>A page opened: a pop-up, or a tab the person opened.</summary>
    public void PageOpened(string page, long at)
    {
        lock (_gate) _pageEvents.Add(new PageEvent("opened", page, null, at, _sequence++));
        Changed();
    }

    /// <summary>The page showed a browser dialog: an alert, a confirm or a prompt.</summary>
    public void BrowserDialog(string page, string message, long at)
    {
        lock (_gate) _pageEvents.Add(new PageEvent("jsDialog", page, message, at, _sequence++));
        Changed();
    }

    /// <summary>Leaves actions out of the recording, with what they caused, as if they had not been done.</summary>
    public void Remove(IEnumerable<string> actionIds)
    {
        lock (_gate) _removed.UnionWith(actionIds);
        Changed();
    }

    /// <summary>
    /// Has the flow find the action's element by its candidate at <paramref name="candidate"/>, whatever the policy
    /// prefers; null lets the policy choose again.
    /// </summary>
    public void Choose(string actionId, int? candidate)
    {
        lock (_gate) _chosen[actionId] = candidate;
        Changed();
    }

    /// <summary>Completes once it is known who started each navigation, or after <paramref name="timeout"/>.</summary>
    public async Task SettleAsync(TimeSpan timeout)
    {
        Task[] pending;
        lock (_gate) pending = [.. _navigationRequests.Select(request => request.ByThePerson)];
        try
        {
            await Task.WhenAll(pending).WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            // A navigation still unknown counts as the app's own.
        }
    }

    /// <summary>The recording so far, each action with what it caused.</summary>
    public Recording Build(string startUrl)
    {
        lock (_gate)
        {
            var actions = _actions.Select(Copy).ToList();
            foreach (var action in actions)
                if (_chosen.TryGetValue(action.Id, out var chosen) && action.Element is { } element)
                    element.Chosen = chosen;

            var byId = actions.ToDictionary(action => action.Id);
            foreach (var effect in _effects)
                if (effect.ActionId is not null && byId.TryGetValue(effect.ActionId, out var action) && Belongs(effect))
                    action.Effects.Add(effect);

            var notRecorded = new List<string>();
            foreach (var pageEvent in PageEventsThatMatter())
                Attribute(pageEvent, actions, notRecorded);

            actions.RemoveAll(action => _removed.Contains(action.Id));

            return new Recording(startUrl, actions) { NotRecorded = notRecorded.Count > 0 ? notRecorded : null };
        }
    }

    private void Changed() => Interlocked.Increment(ref _version);

    private static bool Belongs(CapturedEffect effect) => effect.Kind switch
    {
        "dialogOpened" or "dialogClosed" => effect.SinceAction is null or <= DialogWindowMs,
        "alert" => effect.SinceAction is null or <= AlertWindowMs,
        _ => true,
    };

    // A page's first address is where it opened: a tab opened with an address (a link opened in a new tab) goes there.
    // A later address the page already had (a redirect to itself) is no change.
    private IEnumerable<PageEvent> PageEventsThatMatter()
    {
        var ordered = _pageEvents.OrderBy(pageEvent => pageEvent.At).ThenBy(pageEvent => pageEvent.Sequence).ToList();
        var current = new Dictionary<string, PageEvent>();
        foreach (var pageEvent in ordered)
        {
            if (pageEvent.Kind == "opened")
            {
                var first = ordered.FirstOrDefault(other => other.Kind == "navigated" && other.Page == pageEvent.Page);
                yield return pageEvent with { Detail = first?.Detail is { } url && IsAddress(url) ? url : null };
                continue;
            }

            if (pageEvent.Kind != "navigated")
            {
                yield return pageEvent;
                continue;
            }

            var isFirst = !current.TryGetValue(pageEvent.Page, out var before);
            current[pageEvent.Page] = pageEvent;
            if (!isFirst && before!.Detail != pageEvent.Detail)
                yield return pageEvent with { ByThePerson = StartedByThePerson(pageEvent.Page, before.Sequence, pageEvent.Sequence) };
        }
    }

    // Whether the request for the document the page went to, between its previous address and this one, was the
    // person's own. A change within the page (the app's router) asks for no document, so it never is.
    private bool StartedByThePerson(string page, long after, long upTo) =>
        _navigationRequests.LastOrDefault(request => request.Page == page && request.Sequence > after && request.Sequence < upTo)
            is { ByThePerson: { IsCompletedSuccessfully: true, Result: true } };

    private static bool IsAddress(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps
                                                              || uri.Scheme == Uri.UriSchemeFile);

    // What a page did belongs to the action that caused it on that page; a pop-up, to the action that opened it from
    // any page. An address the person went to is an action of its own, and so is a tab they opened. The app's own
    // navigations (a redirect on load, a sign-in round trip, a slow save) are left out: they happen again when the
    // flow runs, and replaying them would go to a stale address.
    private static void Attribute(PageEvent pageEvent, List<CapturedAction> actions, List<string> notRecorded)
    {
        var cause = pageEvent.ByThePerson
            ? null
            : actions.LastOrDefault(action => action.T <= pageEvent.At + 100
                                              && pageEvent.At - action.T <= NavigationWindowMs
                                              && (pageEvent.Kind == "opened" || action.Page == pageEvent.Page)
                                              && CausesNavigation.Contains(action.Kind));
        // A browser dialog no click or key caused (on leaving a field, say) is answered from the action before it.
        if (cause is null && pageEvent.Kind == "jsDialog")
            cause = actions.LastOrDefault(action => action.T <= pageEvent.At && action.Page == pageEvent.Page);

        if (cause is not null)
        {
            cause.Effects.Add(new CapturedEffect
            {
                Kind = pageEvent.Kind switch { "navigated" => "urlChanged", "opened" => "popup", _ => "jsDialog" },
                ActionId = cause.Id, T = pageEvent.At, Url = pageEvent.Kind == "navigated" ? pageEvent.Detail : null,
                Text = pageEvent.Kind == "jsDialog" ? pageEvent.Detail : null, Page = pageEvent.Page,
            });
            return;
        }

        switch (pageEvent.Kind)
        {
            case "jsDialog":
                notRecorded.Add($"a browser dialog (\"{pageEvent.Detail}\") showed before your first action on {pageEvent.Page}: " +
                                "the flow cannot answer it, and Playwright dismisses it");
                return;
            case "navigated" when !pageEvent.ByThePerson:
                return;
        }

        var action = new CapturedAction
        {
            Id = $"{pageEvent.Kind}-{pageEvent.At}", T = pageEvent.At, Page = pageEvent.Page,
            Kind = pageEvent.Kind == "navigated" ? "navigate" : "newTab", Url = pageEvent.Detail,
        };
        var at = actions.FindIndex(other => other.T > pageEvent.At);
        actions.Insert(at < 0 ? actions.Count : at, action);
    }

    private static CapturedAction Copy(CapturedAction action) =>
        JsonSerializer.Deserialize<CapturedAction>(JsonSerializer.Serialize(action, Recording.Json), Recording.Json)!;

    private sealed record PageEvent(string Kind, string Page, string? Detail, long At, long Sequence)
    {
        /// <summary>For a navigation: the person went there themselves.</summary>
        public bool ByThePerson { get; init; }
    }

    private sealed record NavigationRequest(string Page, long Sequence, Task<bool> ByThePerson);

    // The capture script's message: an action, or an effect of one.
    private sealed class Message
    {
        public string Type { get; set; } = "";
        public string? Id { get; set; }
        public long T { get; set; }
        public string? Kind { get; set; }
        public string? Widget { get; set; }
        public string? Value { get; set; }
        public string? Raw { get; set; }
        public bool? Final { get; set; }
        public bool? Picked { get; set; }
        public bool? Checked { get; set; }
        public string? Key { get; set; }
        public bool? OnPage { get; set; }
        public List<string>? Files { get; set; }
        public string? Url { get; set; }
        public CapturedDialog? Dialog { get; set; }
        public List<string>? OpenDialogs { get; set; }
        public CapturedTarget? Target { get; set; }
        public List<CapturedTarget>? Equivalents { get; set; }
        public CapturedTarget? Field { get; set; }
        public CapturedTarget? Option { get; set; }
        public CapturedTarget? To { get; set; }
        public bool? HoverMenu { get; set; }
        public string? ActionId { get; set; }
        public long? SinceAction { get; set; }
        public string? Text { get; set; }
        public int? AlertCount { get; set; }

        public CapturedAction ToAction(string page)
        {
            var target = LocatorPolicy.Prefer(Target, Equivalents);
            return new()
            {
                Id = Id!, T = T, Kind = Kind ?? "", Page = page, Widget = Widget, Value = Value, Raw = Raw, Final = Final,
                Picked = Picked, Checked = Checked, Key = Key, OnPage = OnPage, Files = Files, Url = Url, Dialog = Dialog,
                OpenDialogs = OpenDialogs, Target = target, Field = Field, Option = Option, To = To, HoverMenu = HoverMenu,
                Equivalents = target == Target ? Equivalents : [Target!, .. Equivalents!.Where(other => other != target)],
            };
        }

        public CapturedEffect ToEffect() => new()
        {
            Kind = Kind ?? "", ActionId = ActionId, SinceAction = SinceAction, T = T, Dialog = Dialog, Text = Text,
            AlertCount = AlertCount, Value = Value, Raw = Raw, Url = Url,
        };
    }
}
