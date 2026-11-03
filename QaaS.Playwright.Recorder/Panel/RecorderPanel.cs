using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.Playwright;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Generation;
using QaaS.Playwright.Recorder.TestRun;

namespace QaaS.Playwright.Recorder.Panel;

/// <summary>
/// The recorder's panel: a window beside the recorded one that shows the flow as it is written, step by step, and
/// takes the person's corrections before it is saved. It is a page of the recording's Chrome in a browser context of
/// its own, so nothing done in it is recorded, and it needs no server or port: its page is embedded in the tool, it is
/// shown each state by <see cref="IPage.EvaluateAsync"/>, and it sends its commands through an exposed function.
/// Closing it leaves the recording going. Its Elements view shows the recorded page's elements, live or as frozen with
/// F4, and adds or re-targets steps on the one the person picks. Test runs the flow so far in a window of its own
/// (<see cref="FlowTester"/>), and shows how each step went. Check puts the recorded page in check mode, as F6 there
/// does; the person deletes steps, renames them, changes their values and drops auto checks here, each change undone
/// with Undo.
/// </summary>
internal sealed class RecorderPanel : IAsyncDisposable
{
    private const string Resource = "QaaS.Playwright.Recorder.Panel.panel.html";

    // The page's side (Panel/ui/src/bridge.ts): it shows a state given as JSON, and sends each command as JSON.
    private const string ShowFunction = "json => typeof window.__qaasPanelShow === 'function' && window.__qaasPanelShow(json)";
    private const string ElementsFunction =
        "json => typeof window.__qaasPanelElements === 'function' && window.__qaasPanelElements(json)";
    private const string SendFunction = "__qaasPanelSend";

    // Playwright highlights until it highlights something else, so highlighting nothing hides it.
    private const string Nothing = "qaas-panel-nothing";

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(150);
    private static readonly Lazy<string> Html = new(LoadHtml);

    private readonly RecordingSession _session;
    private readonly IPage _page;
    private readonly Channel<PanelCommand> _commands = Channel.CreateUnbounded<PanelCommand>(new() { SingleReader = true });
    private readonly TaskCompletionSource _saveRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _stop = new();
    private readonly FlowTester _tester;
    private Task _worker = Task.CompletedTask;

    // The worker's own: what the panel shows, and what changes it besides the recording.
    private PanelSnapshot? _shown;
    private string? _namespace;
    private IPage? _highlighted;
    private string? _error;
    private bool _saving;
    private bool _refresh = true;
    private Picked? _picked;
    private bool _watching;
    private string? _watchedUrl;
    private string _elementsPage = "page";
    private FrozenElements? _frozenShown;
    private int _elementsChanged;

    private RecorderPanel(RecordingSession session, RecordRequest request, IPage page, TestOptions? testOptions)
    {
        _session = session;
        _page = page;
        Request = request;
        _tester = new FlowTester(session, testOptions);
        // Each change of the test run wakes the worker, which shows it.
        _tester.Changed += () => _commands.Writer.TryWrite(new PanelCommand("refresh", null, null, null));
    }

    /// <summary>The request, under the name the person gave the flow in the panel.</summary>
    public RecordRequest Request { get; private set; }

    /// <summary>Completes when the person clicked Save: the recording ends and is saved as when the window closes.</summary>
    public Task SaveRequested => _saveRequested.Task;

    /// <summary>Opens the panel beside the recorded window; <paramref name="testOptions"/> are for its test runs.</summary>
    /// <exception cref="PlaywrightException">The browser could not open it.</exception>
    public static async Task<RecorderPanel> OpenAsync(RecordingSession session, RecordRequest request, TestOptions? testOptions = null)
    {
        var browser = session.Page.Context.Browser ?? throw new PlaywrightException("The recording has no browser.");
        // No viewport, so the page fills its window; no color scheme, so it follows the system's.
        var context = await browser.NewContextAsync(new() { ViewportSize = ViewportSize.NoViewport, ColorScheme = ColorScheme.Null });
        try
        {
            var page = await context.NewPageAsync();
            var panel = new RecorderPanel(session, request, page, testOptions);
            var beside = await PanelWindow.PlaceAsync(session.Page, page);
            await page.ExposeFunctionAsync<string>(SendFunction, panel.Receive);
            await page.SetContentAsync(Html.Value);
            if (beside) await session.Page.BringToFrontAsync();
            session.ElementsChanged += () => Interlocked.Exchange(ref panel._elementsChanged, 1);
            session.CheckingChanged += () => panel._commands.Writer.TryWrite(new PanelCommand("refresh", null, null, null));
            panel._worker = Task.Run(panel.RunAsync);
            return panel;
        }
        catch
        {
            await context.CloseAsync();
            throw;
        }
    }

    /// <summary>The flow's test runs, started from the panel.</summary>
    public FlowTester Tester => _tester;

    /// <summary>Stops updating the panel, and a test run going on; the window closes with the browser.</summary>
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _worker;
        await _tester.DisposeAsync();
        _stop.Dispose();
    }

    // On Playwright's thread: only queued, for the worker.
    private void Receive(string json)
    {
        try
        {
            if (JsonSerializer.Deserialize<PanelCommand>(json, PanelView.Json) is { } command)
                _commands.Writer.TryWrite(command);
        }
        catch (JsonException)
        {
            // Not a command.
        }
    }

    // Shows the recording whenever it changed, and carries out the panel's commands, one at a time.
    private async Task RunAsync()
    {
        var shownVersion = -1L;
        try
        {
            while (!_stop.IsCancellationRequested && !_page.IsClosed)
            {
                await NextAsync();
                var commands = new List<PanelCommand>();
                while (_commands.Reader.TryRead(out var command)) commands.Add(command);
                foreach (var command in commands.Where(command => command.Type is not ("highlight" or "unhighlight")))
                {
                    if (command.Type is "elements" or "pick") await ElementsAsync(command);
                    else if (command.Type == "checkMode" && !_saving) await _session.CheckModeAsync(command.On ?? !_session.Checking);
                    else Carry(command);
                }

                // A highlight replaces the one before, so only the last matters.
                if (commands.LastOrDefault(command => command.Type is "highlight" or "unhighlight") is { } highlight)
                    await HighlightAsync(highlight);
                await ShowElementsChangesAsync();

                var version = _session.Log.Version;
                if (version == shownVersion && !_refresh) continue;
                (shownVersion, _refresh) = (version, false);
                await ShowAsync();
            }
        }
        catch (PlaywrightException)
        {
            // The panel closed, or the browser did.
        }

        // A panel closed while pointing at an element leaves no highlight in the app.
        await HighlightAsync(null);
    }

    private async Task NextAsync()
    {
        using var tick = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        tick.CancelAfter(RefreshInterval);
        try
        {
            await _commands.Reader.WaitToReadAsync(tick.Token);
        }
        catch (OperationCanceledException)
        {
            // Time to look at the recording again, or to stop.
        }
    }

    private void Carry(PanelCommand command)
    {
        if (command.Type == "refresh") _refresh = true;
        // Once saving, the recording is what it is.
        if (_saving) return;
        switch (command.Type)
        {
            case "ready":
                _refresh = true;
                break;
            case "choose" when command.Action is { } action:
                _session.Log.Choose(action, command.Candidate);
                break;
            case "undo" when _shown is { } shown:
                _session.Log.Undo(shown.Undo);
                break;
            case "delete" when command.Action is { } id && _shown is { } shown:
                _session.Log.Remove(shown.Sources.GetValueOrDefault(id) ?? [id], $"Restore {Called(shown, id)}");
                break;
            case "renameStep" when command.Action is { } id:
                var method = StepNamer.MethodName(command.Name);
                if (method is null && !string.IsNullOrWhiteSpace(command.Name)) Refuse("A step's name needs a letter.");
                else _session.Log.Edit(id, "Undo the rename", edit => edit.Step = method);
                break;
            case "editValue" when command is { Action: { } id, Role: { } role }:
                EditValue(id, role, command);
                break;
            case "dropCheck" when command is { Action: { } id, Key: { } key }:
                var drop = command.Drop != false;
                _session.Log.Edit(id, drop ? "Restore the check" : "Drop the check again", edit =>
                {
                    var dropped = edit.Dropped ?? [];
                    if (!drop) dropped.Remove(key);
                    else if (!dropped.Contains(key)) dropped.Add(key);
                    edit.Dropped = dropped.Count > 0 ? dropped : null;
                });
                break;
            case "autoChecks" when command.On is { } on:
                _session.Log.AutoChecks(on);
                break;
            case "rename" when command.Name is { } name:
                try
                {
                    Request = Request.Renamed(name);
                    _error = null;
                }
                catch (ArgumentException invalid)
                {
                    _error = invalid.Message;
                }

                _refresh = true;
                break;
            case "save":
                _saving = _refresh = true;
                _ = _tester.StopAsync();
                _saveRequested.TrySetResult();
                break;
            case "test":
                _namespace ??= ProjectNamespace.Of(Request.OutputDir);
                _ = _tester.RunAsync(Request, _namespace, command.Action, command.Slow == true, _page);
                break;
            case "stopTest":
                _ = _tester.StopAsync();
                break;
            case "clearTest":
                _tester.Clear();
                break;
            case "pause":
                _session.TogglePause();
                break;
            case "choosePicked" when _picked is { Description.Target: { } target }:
                target.Chosen = command.Candidate;
                _refresh = true;
                break;
            case "useForStep" when command.Action is { } action && _picked is { } picked:
                _session.Log.Retarget(action, picked.Description);
                break;
            case "addClick" or "addFill" when _picked is { } picked:
                _session.Log.Insert(NewAction(command, picked), command.Action);
                break;
        }
    }

    // A setting the person renamed, gave another default, or has typed unique per run or as a date from today.
    private void EditValue(string id, string role, PanelCommand command)
    {
        if (_shown?.Actions.GetValueOrDefault(id) is not { } action || action.Parts.FirstOrDefault(part => part.Role == role).Setting is not { } setting)
            return;
        (string Undo, Action<ValueEdit> Change)? edit = command switch
        {
            { Name: { } name } when CSharp.Pascal(name) is null && name.Trim().Length > 0 => null,
            { Name: { } name } => ($"Name it {setting.Name} again", value => value.Name = CSharp.Pascal(name)),
            { Value: { } text } => ("Undo the value", value => value.Value = text),
            { Reset: true } => ("Undo the value", value => value.Value = value.DateFormat is null ? null : value.Value),
            { Unique: { } unique } => (unique ? "Type it as recorded" : "Make it unique again", value => value.Unique = unique ? true : null),
            { Relative: true } when RelativeDate.Of(setting.Text ?? "", RelativeDate.DayOf(action.Source.T)) is { } date =>
                ("Type the date as recorded", value => (value.DateFormat, value.Value) = (date.Format, CSharp.Number(date.Days))),
            { Relative: false } => ("Type it relative to today again", value => (value.DateFormat, value.Value) = (null, null)),
            _ => null,
        };
        if (edit is not var (undo, change))
        {
            if (command.Name is not null) Refuse("A setting's name needs a letter.");
            return;
        }

        _session.Log.Edit(id, undo, actionEdit =>
        {
            var values = actionEdit.Values ??= [];
            var value = values.GetValueOrDefault(role) ?? new ValueEdit();
            change(value);
            values[role] = value;
        });
    }

    private void Refuse(string why) => (_error, _refresh) = (why, true);

    // An action as Undo names it: the click on "Save", the check.
    private static string Called(PanelSnapshot shown, string id)
    {
        var action = shown.View.Steps.SelectMany(step => step.Actions).FirstOrDefault(action => action.Id == id);
        if (action is null || action.Kind == "expect") return "the check";
        return action.Element is { Length: > 0 } element ? $"the {action.Kind} on “{element}”" : $"the {action.Kind}";
    }

    // A click or a fill on the picked element, as the capture script would have recorded it.
    private static CapturedAction NewAction(PanelCommand command, Picked picked)
    {
        var description = JsonSerializer.Deserialize<ElementDescription>(
            JsonSerializer.Serialize(picked.Description, Recording.Json), Recording.Json)!;
        var fill = command.Type == "addFill";
        return new CapturedAction
        {
            Id = $"added-{Guid.NewGuid():N}", Kind = fill ? "fill" : "click", Page = picked.Page,
            Target = description.Target, Dialog = description.Dialog, OpenDialogs = description.OpenDialogs,
            Widget = fill ? description.Target?.Tag == "textarea" ? "textarea" : "text" : null,
            Value = fill ? command.Value ?? "" : null, Raw = fill ? command.Value ?? "" : null, Final = fill ? true : null,
        };
    }

    // The Elements view: the page's elements live, a level at a time, or as frozen with F4, and the one picked.
    private async Task ElementsAsync(PanelCommand command)
    {
        if (command.Type == "pick")
        {
            await PickAsync(command);
            return;
        }

        switch (command.Op)
        {
            case "tree" or "search":
                _watching = command.On ?? _watching;
                var alias = _elementsPage = _session.CurrentPage();
                if (_session.PageNamed(alias) is not { } page) return;
                var answer = command.Op == "search"
                    ? await PageElements.CallAsync(page, new { op = "search", query = command.Query ?? "" })
                    : await PageElements.CallAsync(page, new { op = "tree", ids = command.Ids ?? [] });
                if (_watching) await PageElements.CallAsync(page, new { op = "watch", on = true });
                _watchedUrl = page.Url;
                await ShowElementsAsync(command.Op, alias, answer, page.Url);
                break;
            case "watch":
                _watching = command.On == true;
                if (_session.PageNamed(_session.CurrentPage()) is { } watched)
                    await PageElements.CallAsync(watched, new { op = "watch", on = _watching });
                break;
            case "frozen" when _session.Frozen is { } frozen:
                await ShowFrozenAsync(frozen);
                break;
        }
    }

    private async Task PickAsync(PanelCommand command)
    {
        if (command.Node is not { } node)
        {
            (_picked, _refresh) = (null, true);
            return;
        }

        var frozen = command.Frozen == true ? _session.Frozen : null;
        var alias = frozen?.Page ?? _elementsPage;
        var answer = _session.PageNamed(alias) is { } page
            ? await PageElements.CallAsync(page, new { op = "describe", id = node })
            : null;
        var live = answer is null ? null : JsonSerializer.Deserialize<LiveElement>(answer, Recording.Json);
        // A frozen element no longer on the page keeps the locators found when it was frozen.
        var description = live?.Description ?? frozen?.DescriptionOf(node);
        _picked = description is null ? null : new Picked(alias, node, description, Gone: live is null, live?.Fillable == true);
        _error = description is null ? "That element is not on the page now, and was not one a click acts on when frozen." : null;
        _refresh = true;
    }

    // What changed in the page, or a new freeze: the panel asks again for what it shows.
    private async Task ShowElementsChangesAsync()
    {
        if (_session.Frozen is { } frozen && frozen != _frozenShown) await ShowFrozenAsync(frozen);
        var changed = Interlocked.Exchange(ref _elementsChanged, 0) == 1;
        // A new page, or a new document, has no watcher yet: the panel asks again, which sets one.
        if (_watching && _session.PageNamed(_session.CurrentPage()) is { } page && page.Url != _watchedUrl)
            (changed, _watchedUrl) = (true, page.Url);
        if (changed && _watching) await _page.EvaluateAsync<bool>(ElementsFunction, """{"kind":"changed"}""");
    }

    private async Task ShowFrozenAsync(FrozenElements frozen)
    {
        _frozenShown = frozen;
        var message = new JsonObject
        {
            ["kind"] = "frozen", ["page"] = frozen.Page, ["at"] = frozen.At.ToString("HH:mm:ss"),
            ["data"] = JsonNode.Parse(frozen.Tree),
            ["described"] = new JsonArray([.. frozen.Described.Select(element => JsonValue.Create(element.Id))]),
        };
        await _page.EvaluateAsync<bool>(ElementsFunction, message.ToJsonString());
    }

    private async Task ShowElementsAsync(string kind, string page, string? data, string url)
    {
        if (data is null) return;
        var message = new JsonObject { ["kind"] = kind, ["page"] = page, ["url"] = url, ["data"] = JsonNode.Parse(data) };
        await _page.EvaluateAsync<bool>(ElementsFunction, message.ToJsonString());
    }

    private async Task ShowAsync()
    {
        try
        {
            _namespace ??= ProjectNamespace.Of(Request.OutputDir);
            _shown = PanelSnapshot.Of(_session.Build(), Request, _namespace, Status(), _error);
            _shown = _shown with
            {
                View = _shown.View with
                {
                    Notice = _session.Notice,
                    Element = _picked is { } picked
                        ? PanelSnapshot.Element(picked.Node, picked.Page, picked.Description, picked.Gone, picked.Fillable)
                        : null,
                    Test = _tester.Report is { } test ? PanelTest.Of(test, _shown.View.Code) : null,
                    Checking = _session.Checking,
                    Undo = _session.Log.NextUndo(_shown.Undo.Count > 0),
                },
            };
        }
        catch (Exception failure)
        {
            // A recording the writer cannot write cannot be saved either: the panel says why, and the recording goes on.
            var last = _shown ?? PanelSnapshot.Of(new Recording(Request.Url, []), Request, _namespace ?? "Flows", Status(), null);
            _shown = last with { View = last.View with { Error = $"Could not write the flow: {failure.Message}" } };
        }

        var json = JsonSerializer.Serialize(_shown.View, PanelView.Json);
        if (await _page.EvaluateAsync<bool>(ShowFunction, json)) return;
        // The page lost the panel, e.g. it was reloaded: put it back.
        await _page.SetContentAsync(Html.Value);
        await _page.EvaluateAsync<bool>(ShowFunction, json);
    }

    private string Status() => _saving ? "saving" : _session.State switch
    {
        RecordingState.Paused => "paused",
        RecordingState.LoggingIn => "login",
        RecordingState.Testing => "testing",
        _ => "recording",
    };

    // Shows in the recorded page the element of an action, what one of its candidates finds, or an element of the
    // Elements view, or a candidate of the one picked there; null hides it.
    private async Task HighlightAsync(PanelCommand? command)
    {
        var action = command is { Type: "highlight", Action: { } id } ? _shown?.Actions.GetValueOrDefault(id) : null;
        var candidate = command?.Candidate is { } index ? action?.Source.Element?.Candidates.ElementAtOrDefault(index) : action?.Locator;
        var page = action is null ? null : _session.PageNamed(action.Source.Page);
        string? selector = null;
        if (command is { Type: "highlight", Action: null, Node: { } node })
        {
            page = _session.PageNamed(command.Frozen == true ? _session.Frozen?.Page ?? "page" : _elementsPage);
            selector = page is null ? null : await PageElements.CallAsync(page, new { op = "selector", id = node });
        }
        else if (command is { Type: "highlight", Action: null, Candidate: { } pickedIndex } && _picked is { } picked)
        {
            page = _session.PageNamed(picked.Page);
            candidate = picked.Description.Target?.Candidates.ElementAtOrDefault(pickedIndex);
        }

        try
        {
            if (_highlighted is { IsClosed: false } previous && previous != page) await previous.Locator(Nothing).HighlightAsync();
            _highlighted = page;
            if (page is not null)
                await ((selector is not null ? page.Locator(selector) : candidate is null ? null : Locate(page, candidate))
                       ?? page.Locator(Nothing)).HighlightAsync();
        }
        catch (Exception failure) when (failure is PlaywrightException or ArgumentException)
        {
            // The page closed or is navigating, or the locator is not one .NET can build: there is nothing to show.
        }
    }

    // The candidate as a Playwright locator, as the flow would build it from the page.
    private static ILocator? Locate(IPage page, CapturedCandidate candidate)
    {
        if (candidate.Kind == "generator") return candidate.Selector is { Length: > 0 } selector ? page.Locator(selector) : null;
        ILocator? locator = null;
        foreach (var part in candidate.Parts)
        {
            var role = Enum.TryParse<AriaRole>(part.Role, ignoreCase: true, out var known) ? known : (AriaRole?)null;
            var name = part.Regex is null ? part.Name : null;
            var nameRegex = part.Regex is { } regex ? new Regex(regex) : null;
            locator = (part.M, locator, role) switch
            {
                ("css", null, _) => page.Locator(part.Css ?? ""),
                ("css", { } scope, _) => scope.Locator(part.Css ?? ""),
                ("testid", null, _) => page.GetByTestId(part.Value ?? ""),
                ("testid", { } scope, _) => scope.GetByTestId(part.Value ?? ""),
                ("role", null, { } aria) => page.GetByRole(aria, new() { Name = name, NameRegex = nameRegex, Exact = part.Exact }),
                ("role", { } scope, { } aria) => scope.GetByRole(aria, new() { Name = name, NameRegex = nameRegex, Exact = part.Exact }),
                ("filter", { } scope, _) => scope.Filter(new() { HasText = part.HasText }),
                _ => null,
            };
            if (locator is null) return null;
        }

        return locator;
    }

    private static string LoadHtml()
    {
        using var stream = typeof(RecorderPanel).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"The embedded {Resource} is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>What the panel's page asks for.</summary>
    private sealed record PanelCommand(string Type, string? Action, int? Candidate, string? Name)
    {
        /// <summary>For the Elements view: <c>tree</c>, <c>search</c>, <c>watch</c> or <c>frozen</c>.</summary>
        public string? Op { get; init; }

        /// <summary>An element of the Elements view, by the capture script's id for it.</summary>
        public int? Node { get; init; }

        /// <summary>The elements whose children the view shows.</summary>
        public int[]? Ids { get; init; }

        public string? Query { get; init; }

        /// <summary>The text an added fill types.</summary>
        public string? Value { get; init; }

        /// <summary>The element is one of the frozen ones.</summary>
        public bool? Frozen { get; init; }

        /// <summary>Whether the view follows the page as it changes.</summary>
        public bool? On { get; init; }

        /// <summary>For a test run: in slow motion.</summary>
        public bool? Slow { get; init; }

        /// <summary>For an edit of a setting: its part in the action (<c>value</c>, <c>row</c>, ...).</summary>
        public string? Role { get; init; }

        /// <summary>For an edit of a setting: back to the value recorded.</summary>
        public bool? Reset { get; init; }

        /// <summary>For an edit of a setting: typed unique per run, or as recorded.</summary>
        public bool? Unique { get; init; }

        /// <summary>For an edit of a setting: typed as days from today, or as recorded.</summary>
        public bool? Relative { get; init; }

        /// <summary>For an auto check: its key.</summary>
        public string? Key { get; init; }

        /// <summary>For an auto check: dropped, or back.</summary>
        public bool? Drop { get; init; }
    }

    /// <summary>The element picked in the Elements view: where it is, and how to locate it.</summary>
    private sealed record Picked(string Page, int Node, ElementDescription Description, bool Gone, bool Fillable);

    /// <summary>The capture script's answer about an element on the page now.</summary>
    private sealed class LiveElement
    {
        public ElementDescription? Description { get; set; }
        public bool Fillable { get; set; }
    }
}
