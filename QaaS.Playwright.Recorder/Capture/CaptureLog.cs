using System.Text.Json;
using QaaS.Playwright.Recorder.Generation;

namespace QaaS.Playwright.Recorder.Capture;

/// <summary>
/// Collects what the capture script reports from every page, and what Playwright sees itself (navigations, pop-ups,
/// browser dialogs), then ties each change of the page to the action that caused it. While paused it collects nothing,
/// and what happened meanwhile is no action's doing. Made from a recording it continues, it starts with that one's
/// actions, and adds what is recorded after them. It also keeps the person's changes in the panel, each of which
/// <see cref="Undo"/> takes back, newest first, in turn with the actions recorded, and the parts they recorded again
/// (<see cref="BeginSplice"/>): what is recorded then goes in place of the part, before the actions after it. Safe to
/// call from any thread.
/// </summary>
internal sealed class CaptureLog(bool paused = false, Recording? from = null)
{
    // An action causes what follows it this soon; later, the page changed by itself or the person did something else.
    private const long DialogWindowMs = 5_000;
    private const long AlertWindowMs = 2_500;
    private const long NavigationWindowMs = 5_000;

    private static readonly HashSet<string> CausesNavigation =
        ["click", "dblclick", "press", "choose", "check", "uncheck", "drag"];

    private readonly Lock _gate = new();
    private readonly List<CapturedAction> _actions = [.. from?.Actions ?? []];
    private readonly Dictionary<string, int> _indexById = (from?.Actions ?? []).Select((action, index) => (action.Id, index))
        .DistinctBy(pair => pair.Id).ToDictionary();
    private readonly IReadOnlyList<string> _notRecordedBefore = from?.NotRecorded ?? [];
    private readonly List<CapturedEffect> _effects = [];
    private readonly List<PageEvent> _pageEvents = [];
    private readonly List<NavigationRequest> _navigationRequests = [];
    private readonly HashSet<string> _removed = [];
    private readonly Dictionary<string, int?> _chosen = [];
    private readonly Dictionary<string, ElementDescription> _retargeted = [];
    private readonly Dictionary<string, ActionEdit> _edits = [];
    private readonly List<Change> _changes = [];
    private readonly List<(long From, long To)> _pauses = [];
    private readonly List<Splice> _splices = [];
    private bool? _autoChecks = from?.AutoChecks;
    private long _sequence;
    private long _version;
    private bool _paused = paused;

    // Created paused, the log starts recording when first resumed, on the pages as they are then.
    private bool _started = !paused;
    private long _pausedAt;
    private long _resumedAt;

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
            // While paused, only an action begun before the pause is completed, e.g. the field being typed into. One begun
            // while paused stays out when told later, as an action of a replay may be, and so does what it caused.
            if (_paused && !(message.Type == "action" && message.Id is not null && _indexById.ContainsKey(message.Id)))
                return null;
            var began = message.Type == "effect" ? message.T - (message.SinceAction ?? 0) : message.T;
            if (began < _resumedAt && !(message.Type == "action" && message.Id is not null && _indexById.ContainsKey(message.Id)))
                return null;
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
            OpenSplice()?.Members.Add(action.Id);
            return action.Final == false ? null : action;
        }
    }

    /// <summary>A page went to another address, by a link, a script or the address bar.</summary>
    public void Navigated(string page, string url, long at) => AddPageEvent("navigated", page, url, at);

    /// <summary>
    /// A page asked for a new document. <paramref name="byThePerson"/> tells, once the request's headers are known,
    /// whether the person started it (the address bar, a bookmark) rather than a link, a form or the app's script.
    /// </summary>
    public void NavigationRequested(string page, Task<bool> byThePerson)
    {
        lock (_gate)
        {
            if (_paused) return;
            _navigationRequests.Add(new NavigationRequest(page, _sequence++, byThePerson));
        }

        Changed();
        // Who started it tells whether it is an action of its own, once it is known.
        _ = byThePerson.ContinueWith(_ => Changed(), TaskScheduler.Default);
    }

    /// <summary>
    /// A page opened: a pop-up, or a tab the person opened, as <paramref name="byThePerson"/> tells once it is known: a
    /// tab of their own is no pop-up of the action before it, however soon after it.
    /// </summary>
    public void PageOpened(string page, long at, Task<bool>? byThePerson = null)
    {
        lock (_gate)
        {
            if (_paused) return;
            _pageEvents.Add(new PageEvent("opened", page, null, at, _sequence++) { OpenedByThePerson = byThePerson });
        }

        Changed();
        _ = byThePerson?.ContinueWith(_ => Changed(), TaskScheduler.Default);
    }

    /// <summary>The page showed a browser dialog: an alert, a confirm or a prompt.</summary>
    public void BrowserDialog(string page, string message, long at) => AddPageEvent("jsDialog", page, message, at);

    public bool Paused
    {
        get { lock (_gate) return _paused; }
    }

    /// <summary>Records nothing until <see cref="Resume"/>.</summary>
    public void Pause(long at)
    {
        lock (_gate)
        {
            if (_paused) return;
            (_paused, _pausedAt) = (true, at);
        }

        Changed();
    }

    /// <summary>
    /// Records again. <paramref name="pages"/> are the open pages and their addresses now. A page that went to another
    /// address while paused, or opened meanwhile, is gone to by that address, as the person would type it: the step
    /// after the pause acts on that page, and a replay would otherwise still be where the pause began. How the app got
    /// there was not recorded, so the flow lists it to check by hand. The first time a log created paused resumes, the
    /// recording starts on the pages as they are. <paramref name="rebase"/> starts it so too after a pause: the flow was
    /// replayed meanwhile, up to where it goes on (<see cref="BeginSplice"/>), so the pages are where it leaves them.
    /// </summary>
    public void Resume(IReadOnlyCollection<(string Page, string Url)> pages, long at, bool rebase = false)
    {
        lock (_gate)
        {
            if (!_paused) return;
            (_paused, _resumedAt) = (false, at);
            if (_started && rebase)
            {
                _pauses.Add((_pausedAt, at));
                foreach (var (page, url) in pages) _pageEvents.Add(new PageEvent("navigated", page, url, at, _sequence++));
            }
            else if (!_started)
            {
                _started = true;
                // What the pages do from now on is none of a continued recording's actions' doing.
                if (_actions.Count > 0) _pauses.Add((_actions.Max(action => action.T), at));
                foreach (var (page, url) in pages) _pageEvents.Add(new PageEvent("navigated", page, url, at, _sequence++));
            }
            else
            {
                _pauses.Add((_pausedAt, at));
                foreach (var (page, url) in pages)
                {
                    var before = _pageEvents.LastOrDefault(pageEvent => pageEvent.Page == page && pageEvent.Kind == "navigated");
                    if (_pageEvents.All(pageEvent => pageEvent.Page != page))
                        _pageEvents.Add(new PageEvent("opened", page, null, at, _sequence++) { Resumed = true });
                    if (before?.Detail != url)
                        _pageEvents.Add(new PageEvent("navigated", page, url, at, _sequence++) { Resumed = before is not null });
                }
            }
        }

        Changed();
    }

    /// <summary>
    /// Has the action act on another element, as the capture script described it, e.g. one the person picked in the
    /// panel's Elements view.
    /// </summary>
    public void Retarget(string actionId, ElementDescription element)
    {
        lock (_gate)
        {
            Remember("Act on the element it acted on before", Restore(_retargeted, actionId), Restore(_chosen, actionId));
            _retargeted[actionId] = Copy(element);
            // A choice among the other element's locators means nothing for this one's.
            _chosen.Remove(actionId);
        }

        Changed();
    }

    /// <summary>
    /// Adds an action the person did not do in the app but added in the panel: after the action
    /// <paramref name="afterId"/>, or last. It caused nothing on the page.
    /// </summary>
    public void Insert(CapturedAction action, string? afterId)
    {
        lock (_gate)
        {
            // While a part is recorded again, last is where what is recorded goes, and so is the start of the part.
            var open = OpenSplice();
            var member = open is not null && (afterId is null || afterId == open.After || open.Members.Contains(afterId));
            afterId = afterId is null && open is not null
                ? _actions.LastOrDefault(other => open.Members.Contains(other.Id))?.Id ?? open.After
                : afterId;
            var at = afterId is not null && _indexById.TryGetValue(afterId, out var after) ? after + 1
                : open is not null ? 0
                : _actions.Count;
            action.Added = true;
            action.T = at > 0 ? _actions[at - 1].T : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _actions.Insert(at, action);
            for (var index = at; index < _actions.Count; index++) _indexById[_actions[index].Id] = index;
            if (member) open!.Members.Add(action.Id);
            Remember($"Remove the {action.Kind} added", () => _removed.Add(action.Id));
        }

        Changed();
    }

    /// <summary>
    /// Records a part of the flow again, or more of it after an action: what is recorded from now on, until
    /// <see cref="EndSplice"/>, goes in place of the recorded actions <paramref name="replaced"/>, after the action
    /// <paramref name="after"/> and before <paramref name="before"/> (null: at the end), and the actions after it stay.
    /// <paramref name="label"/> names the part as Undo says it. Undone before anything is recorded in it, the part is
    /// kept as it was. One part at a time: a part begun ends the one before, kept.
    /// </summary>
    public void BeginSplice(string label, IReadOnlyCollection<string> replaced, string? after, string? before)
    {
        lock (_gate)
        {
            if (OpenSplice() is { } open) Close(open, keep: true);
            var splice = new Splice(label, [.. replaced], after, before, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            _splices.Add(splice);
            splice.Change = Remember($"Keep {label} as it was", () => splice.Undone = true);
        }

        Changed();
    }

    /// <summary>
    /// Ends the part begun with <see cref="BeginSplice"/>: what was recorded in it stays in its place, and Undo then
    /// brings the part back as it was; or, not <paramref name="keep"/>, the part is as it was before. Returns whether a
    /// part was being recorded.
    /// </summary>
    public bool EndSplice(bool keep)
    {
        lock (_gate)
        {
            if (OpenSplice() is not { } open) return false;
            Close(open, keep);
        }

        Changed();
        return true;
    }

    /// <summary>A part is recorded again, or more after an action (<see cref="BeginSplice"/>).</summary>
    public bool Splicing
    {
        get { lock (_gate) return OpenSplice() is not null; }
    }

    /// <summary>
    /// The actions of <paramref name="recording"/> recorded in the part being recorded again, or after the action it
    /// goes on from; empty when none is.
    /// </summary>
    public IReadOnlySet<string> Fresh(Recording recording)
    {
        lock (_gate)
            return OpenSplice() is { } open
                ? recording.Actions.Where(action => InSegment(open, action)).Select(action => action.Id).ToHashSet()
                : new HashSet<string>();
    }

    /// <summary>The page of the last action, or null before the first.</summary>
    public string? LastPage()
    {
        lock (_gate) return _actions.LastOrDefault()?.Page;
    }

    /// <summary>
    /// Leaves actions out of the recording, with what they caused, as if they had not been done. A deletion
    /// (<paramref name="undo"/> says how it reads) can be undone; the last step undone cannot.
    /// </summary>
    public void Remove(IReadOnlyCollection<string> actionIds, string? undo = null)
    {
        lock (_gate)
        {
            var removed = actionIds.Where(_removed.Add).ToList();
            if (undo is not null) Remember(undo, () => _removed.ExceptWith(removed));
        }

        Changed();
    }

    /// <summary>
    /// Has the flow find the action's element by its candidate at <paramref name="candidate"/>, whatever the policy
    /// prefers; null lets the policy choose again.
    /// </summary>
    public void Choose(string actionId, int? candidate)
    {
        lock (_gate)
        {
            Remember("Use the locator chosen before", Restore(_chosen, actionId));
            _chosen[actionId] = candidate;
        }

        Changed();
    }

    /// <summary>Changes what the person changed about the action; <paramref name="undo"/> says how undoing it reads.</summary>
    public void Edit(string actionId, string undo, Action<ActionEdit> change)
    {
        lock (_gate)
        {
            Remember(undo, Restore(_edits, actionId));
            // A recording continued comes with its edits.
            var known = _edits.GetValueOrDefault(actionId)
                        ?? (_indexById.TryGetValue(actionId, out var index) ? _actions[index].Edit : null);
            var edit = known is null ? new ActionEdit() : Copy(known);
            change(edit);
            _edits[actionId] = edit;
        }

        Changed();
    }

    /// <summary>Turns every auto check on or off, as <c>--no-auto-checks</c> does; null leaves it to the command line.</summary>
    public void AutoChecks(bool? on)
    {
        lock (_gate)
        {
            var before = _autoChecks;
            Remember(on == false ? "Turn the auto checks on" : "Turn the auto checks off", () => _autoChecks = before);
            _autoChecks = on;
        }

        Changed();
    }

    /// <summary>
    /// What <see cref="Undo"/> takes back, as the panel says it: the person's last change, or else the last step (null
    /// when the recording has none).
    /// </summary>
    public string? NextUndo(bool hasSteps)
    {
        lock (_gate) return LastChange() is { } change ? change.Undo : hasSteps ? "Remove the last step" : null;
    }

    /// <summary>
    /// Takes back the person's last change in the panel, or, when an action was recorded since, leaves
    /// <paramref name="lastStep"/> (the last step and what led to it) out of the recording.
    /// </summary>
    public void Undo(IReadOnlyCollection<string> lastStep)
    {
        lock (_gate)
        {
            if (LastChange() is { } change)
            {
                _changes.Remove(change);
                change.Restore();
            }
            else
                _removed.UnionWith(lastStep);
        }

        Changed();
    }

    /// <summary>Completes once it is known who started each navigation, or after <paramref name="timeout"/>.</summary>
    public async Task SettleAsync(TimeSpan timeout)
    {
        Task[] pending;
        lock (_gate)
            pending = [.. _navigationRequests.Select(request => request.ByThePerson),
                .. _pageEvents.Select(pageEvent => pageEvent.OpenedByThePerson).OfType<Task<bool>>()];
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
            {
                if (_retargeted.TryGetValue(action.Id, out var retargeted)) Retarget(action, retargeted);
                if (_chosen.TryGetValue(action.Id, out var chosen) && action.Element is { } element)
                    element.Chosen = chosen;
                if (_edits.TryGetValue(action.Id, out var edit)) action.Edit = Copy(edit);
            }

            var byId = actions.ToDictionary(action => action.Id);
            foreach (var effect in _effects)
                if (effect.ActionId is not null && byId.TryGetValue(effect.ActionId, out var action) && Belongs(effect))
                    action.Effects.Add(effect);

            var notRecorded = new List<string>(_notRecordedBefore);
            foreach (var pageEvent in PageEventsThatMatter())
                Attribute(pageEvent, actions, notRecorded, _pauses);
            foreach (var splice in _splices) Place(splice, actions);

            actions.RemoveAll(action => _removed.Contains(action.Id));

            return new Recording(startUrl, actions)
            {
                NotRecorded = notRecorded.Count > 0 ? notRecorded : null, AutoChecks = _autoChecks,
            };
        }
    }

    private Splice? OpenSplice() => _splices.LastOrDefault(splice => splice is { Until: null, Undone: false });

    // A part kept is undone whole, newest first with the changes after it; one not kept was never there.
    private void Close(Splice splice, bool keep)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _changes.Remove(splice.Change!);
        if (!keep || (splice.Replaced.Count == 0 && !_actions.Any(action => InSegment(splice, action))))
        {
            splice.Undone = true;
            return;
        }

        splice.Until = now;
        splice.Change = Remember($"Bring back {splice.Label} as it was", () => splice.Undone = true);
    }

    // What the part recorded: the actions recorded or added in it, and the navigations and tabs it made.
    private bool InSegment(Splice splice, CapturedAction action) =>
        splice.Members.Contains(action.Id)
        || (!_indexById.ContainsKey(action.Id) && action.T >= splice.Since && action.T <= (splice.Until ?? long.MaxValue));

    // The part recorded goes before the action that followed the part it replaces, in the order it was recorded; the
    // actions it replaces go. A part kept as it was leaves the recording as it was.
    private void Place(Splice splice, List<CapturedAction> actions)
    {
        var segment = actions.Where(action => InSegment(splice, action)).ToList();
        actions.RemoveAll(action => InSegment(splice, action));
        if (splice.Undone) return;
        actions.RemoveAll(action => splice.Replaced.Contains(action.Id));
        var before = splice.Before is null ? -1 : actions.FindIndex(action => action.Id == splice.Before);
        actions.InsertRange(before < 0 ? actions.Count : before, segment);
    }

    private void Changed() => Interlocked.Increment(ref _version);

    // A change of the person's, newer than every action recorded: one recorded since is undone first.
    private Change? LastChange()
    {
        var change = _changes.LastOrDefault();
        var lastAction = _actions.Where(action => !_removed.Contains(action.Id) && !_splices.Any(splice => splice.Undone && splice.Members.Contains(action.Id)))
            .Select(action => action.T).DefaultIfEmpty().Max();
        return change is not null && change.At >= lastAction ? change : null;
    }

    private Change Remember(string undo, params Action[] restore)
    {
        var change = new Change(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), undo, () =>
        {
            foreach (var step in restore) step();
        });
        _changes.Add(change);
        return change;
    }

    // Puts the entry back as it is now: the value, or none.
    private static Action Restore<T>(Dictionary<string, T> values, string key)
    {
        var had = values.TryGetValue(key, out var before);
        return () =>
        {
            if (had) values[key] = before!;
            else values.Remove(key);
        };
    }

    private void AddPageEvent(string kind, string page, string? detail, long at)
    {
        lock (_gate)
        {
            if (_paused) return;
            _pageEvents.Add(new PageEvent(kind, page, detail, at, _sequence++));
        }

        Changed();
    }

    // A copy of the description, so the action's own changes (the person's choice of locator) stay its own.
    internal static void Retarget(CapturedAction action, ElementDescription element)
    {
        var copy = Copy(element);
        if (action.Kind == "choose" && action.Field is not null) action.Field = copy.Target;
        else action.Target = copy.Target;
        (action.Dialog, action.OpenDialogs, action.Equivalents) = (copy.Dialog, copy.OpenDialogs, null);
    }

    private static bool Belongs(CapturedEffect effect) => effect.Kind switch
    {
        "dialogOpened" or "dialogClosed" or "busy" => effect.SinceAction is null or <= DialogWindowMs,
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
                yield return pageEvent with
                {
                    Detail = first?.Detail is { } url && IsAddress(url) ? url : null,
                    ByThePerson = pageEvent.OpenedByThePerson is { IsCompletedSuccessfully: true, Result: true },
                };
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
                yield return pageEvent with
                {
                    ByThePerson = pageEvent.Resumed || StartedByThePerson(pageEvent.Page, before.Sequence, pageEvent.Sequence),
                };
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
    // flow runs, and replaying them would go to a stale address. Nothing is caused across a pause, nor by an action
    // added in the panel, nor by a check.
    private static void Attribute(PageEvent pageEvent, List<CapturedAction> actions, List<string> notRecorded,
        List<(long From, long To)> pauses)
    {
        bool CouldCause(CapturedAction action) => !action.Added && action.Kind != "expect"
                                                  && !pauses.Any(pause => action.T <= pause.From && pause.From <= pageEvent.At);
        var cause = pageEvent.ByThePerson || pageEvent.Resumed
            ? null
            : actions.LastOrDefault(action => action.T <= pageEvent.At + 100
                                              && pageEvent.At - action.T <= NavigationWindowMs
                                              && (pageEvent.Kind == "opened" || action.Page == pageEvent.Page)
                                              && CausesNavigation.Contains(action.Kind) && CouldCause(action));
        // A browser dialog no click or key caused (on leaving a field, say) is answered from the action before it.
        if (cause is null && pageEvent.Kind == "jsDialog")
            cause = actions.LastOrDefault(action => action.T <= pageEvent.At && action.Page == pageEvent.Page && CouldCause(action));

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
            Id = $"{pageEvent.Kind}-{pageEvent.At}-{pageEvent.Page}", T = pageEvent.At, Page = pageEvent.Page,
            Kind = pageEvent.Kind == "navigated" ? "navigate" : "newTab", Url = pageEvent.Detail,
            AfterPause = pageEvent.Resumed ? true : null,
        };
        var at = actions.FindIndex(other => other.T > pageEvent.At);
        actions.Insert(at < 0 ? actions.Count : at, action);
    }

    private static T Copy<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Recording.Json), Recording.Json)!;

    private sealed record PageEvent(string Kind, string Page, string? Detail, long At, long Sequence)
    {
        /// <summary>For a navigation: the person went there themselves.</summary>
        public bool ByThePerson { get; init; }

        /// <summary>Where a page was when the recording resumed, having changed while it was paused.</summary>
        public bool Resumed { get; init; }

        /// <summary>For a page opened: whether the person opened it, a tab of their own, once it is known.</summary>
        public Task<bool>? OpenedByThePerson { get; init; }
    }

    private sealed record NavigationRequest(string Page, long Sequence, Task<bool> ByThePerson);

    /// <summary>
    /// A part recorded again, or recorded after an action: the actions it replaces, where what is recorded in it goes,
    /// since when it records, until when once it ended, and the actions recorded or added in it.
    /// </summary>
    private sealed class Splice(string label, HashSet<string> replaced, string? after, string? before, long since)
    {
        public string Label { get; } = label;
        public HashSet<string> Replaced { get; } = replaced;

        /// <summary>The action before the part, which an action added at its start follows; null at the flow's start.</summary>
        public string? After { get; } = after;

        /// <summary>The action after the part, which what is recorded in it goes before; null at the end.</summary>
        public string? Before { get; } = before;

        public long Since { get; } = since;
        public long? Until { get; set; }
        public HashSet<string> Members { get; } = [];

        /// <summary>Kept as it was: by Esc, or by Undo.</summary>
        public bool Undone { get; set; }

        /// <summary>The change Undo takes back to keep the part as it was.</summary>
        public Change? Change { get; set; }
    }

    /// <summary>A change the person made in the panel: when, how undoing it reads, and what undoes it.</summary>
    private sealed record Change(long At, string Undo, Action Restore);

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
        public string? Opener { get; set; }
        public CapturedScroll? Scrolled { get; set; }
        public CapturedTarget? To { get; set; }
        public bool? HoverMenu { get; set; }
        public CapturedTarget? HoverOpener { get; set; }
        public string? OptionCss { get; set; }
        public bool? Committed { get; set; }
        public bool? Focus { get; set; }
        public bool? Watched { get; set; }
        public bool? Multi { get; set; }
        public string? ActionId { get; set; }
        public long? SinceAction { get; set; }
        public string? Text { get; set; }
        public int? AlertCount { get; set; }
        public string? Check { get; set; }
        public CapturedTarget? Group { get; set; }

        public CapturedAction ToAction(string page)
        {
            var target = LocatorPolicy.Prefer(Target, Equivalents);
            return new()
            {
                Id = Id!, T = T, Kind = Kind ?? "", Page = page, Widget = Widget, Value = Value, Raw = Raw, Final = Final,
                Picked = Picked, Checked = Checked, Key = Key, OnPage = OnPage, Files = Files, Url = Url, Dialog = Dialog,
                OpenDialogs = OpenDialogs, Target = target, Field = Field, Option = Option, Opener = Opener, Scrolled = Scrolled, To = To, HoverMenu = HoverMenu,
                HoverOpener = HoverOpener, OptionCss = OptionCss, Committed = Committed, Focus = Focus, Watched = Watched, Check = Check, Group = Group, Equivalents = target == Target ? Equivalents : [Target!, .. Equivalents!.Where(other => other != target)],
            };
        }

        public CapturedEffect ToEffect() => new()
        {
            Kind = Kind ?? "", ActionId = ActionId, SinceAction = SinceAction, T = T, Dialog = Dialog, Text = Text,
            AlertCount = AlertCount, Value = Value, Raw = Raw, Url = Url, Multi = Multi,
        };
    }
}
