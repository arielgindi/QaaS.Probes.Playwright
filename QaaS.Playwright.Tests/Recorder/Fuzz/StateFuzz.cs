using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using QaaS.Playwright.Recorder;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Generation;
using QaaS.Playwright.Recorder.Login;
using QaaS.Playwright.Recorder.Panel;
using QaaS.Playwright.Recorder.TestRun;
using QaaS.Playwright.Tests.EndToEnd;

namespace QaaS.Playwright.Tests.Recorder.Fuzz;

/// <summary>
/// Drives a real recording with its panel, in a headless Chrome on the tests' own site, through a sequence of what a
/// person does: actions in the app with the real mouse and keys, the modes' keys there and in the panel, the panel's
/// edits, tests, parts recorded again, a login flow and a recording continued, and the end of the recording. After each
/// step it checks that the recorder is in one state everywhere (its model, the console, the panel and each page's
/// overlay), that nothing is recorded while it says so, that its autosave reloads to the recording, that Undo takes an
/// edit back, and that it never stays in a mode nobody can see or leave; at the end, that the flow saves as it was and
/// builds. A sequence is a list of steps, each a short text (<see cref="Step"/>), written <c>a | b | c</c>; the first may
/// say how the recording starts (<c>start:login</c>, <c>start:hang</c>, <c>start:continue</c>, <c>start:/orders</c>), and
/// the last how it ends (<c>save:ctrl-s</c>, <c>save:enter</c>, <c>save:close</c>, <c>save:ctrl-c</c>). A step that
/// starts with <c>!</c> is followed at once by the next, without the checks, as when keys come fast.
/// </summary>
internal sealed class StateFuzz : IAsyncDisposable
{
    private const int Agreement = 5000;
    private static readonly SemaphoreSlim Shared = new(1, 1);
    private static TestSite? _site;
    private static string? _continued;
    private static string? _folder;

    private readonly int _seed;
    private readonly Random _random;
    private readonly List<string> _done = [];
    private readonly List<string> _log = [];
    private readonly Lock _gate = new();
    private RecordingSession _session = null!;
    private RecorderPanel _panel = null!;
    private IPage _view = null!;
    private RecordRequest _request = null!;
    private Autosave _autosave = null!;
    private readonly CancellationTokenSource _stopWatching = new();
    private Task _watching = Task.CompletedTask;
    private Task<FlowRecorder.Replayed> _starting = Task.FromResult(FlowRecorder.Replayed.All);
    private RecordingState? _announced;
    private int _announcements;
    private string _current = "page";
    private string? _undoTo;
    private bool _ended;
    private bool _mustPause;
    private string? _partStart;
    private bool _editedInPart;
    private readonly List<string> _unhandled = [];

    private StateFuzz(int seed)
    {
        _seed = seed;
        _random = new Random(seed);
    }

    /// <summary>What the last sequence that passed did, step by step.</summary>
    public static IReadOnlyList<string> LastLog { get; private set; } = [];

    /// <summary>The flows saved so far, by class name, for one strict build of them all.</summary>
    public static List<GeneratedFlow> Saved { get; } = [];

    /// <summary>Runs <paramref name="sequence"/>; returns null when every check held, else what broke, at which step.</summary>
    public static Task<Failure?> RunAsync(string sequence, int seed = 0) =>
        RunAsync(seed, Steps(sequence), generate: 0);

    /// <summary>Runs a sequence made up as it goes, from <paramref name="seed"/>, of <paramref name="steps"/> steps.</summary>
    public static Task<Failure?> FuzzAsync(int seed, int steps) => RunAsync(seed, [], steps);

    /// <summary>The steps of a sequence's text.</summary>
    public static List<string> Steps(string sequence) =>
        [.. sequence.Split('|').Select(step => step.Trim()).Where(step => step.Length > 0)];

    /// <summary>What broke: its kind, which tells two failures apart, what was seen, and the steps done up to it.</summary>
    public sealed record Failure(string Kind, string What, IReadOnlyList<string> Steps, IReadOnlyList<string> Log)
    {
        public string Sequence => string.Join(" | ", Steps);

        public override string ToString() =>
            $"{Kind}: {What}\n  sequence: {Sequence}\n  log:\n    {string.Join("\n    ", Log.TakeLast(40))}";
    }

    private sealed class Broken(string kind, string what) : Exception(what)
    {
        public string Kind { get; } = kind;
    }

    private static async Task<Failure?> RunAsync(int seed, List<string> given, int generate)
    {
        await using var fuzz = new StateFuzz(seed);
        var steps = given.Count > 0 ? given : [];
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs unobserved)
        {
            if (unobserved.Exception.ToString().Contains("QaaS.Playwright.Recorder", StringComparison.Ordinal))
                lock (fuzz._unhandled) fuzz._unhandled.Add(unobserved.Exception.ToString());
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            var start = steps.FirstOrDefault(step => step.StartsWith("start:", StringComparison.Ordinal))
                        ?? (generate > 0 ? fuzz.StartStep() : "start:/helpdesk");
            await fuzz.StartAsync(start);
            fuzz._done.Add(start);
            var body = generate > 0 ? null : steps.Where(step => !step.StartsWith("start:", StringComparison.Ordinal)).ToList();
            var count = body?.Count ?? generate;
            for (var index = 0; index < count; index++)
            {
                var step = body?[index] ?? await fuzz.NextAsync();
                fuzz._done.Add(step);
                await fuzz.DoAsync(step, check: !step.StartsWith('!'));
                if (fuzz._ended) break;
            }

            if (!fuzz._ended) await fuzz.DoAsync(generate > 0 ? fuzz.SaveStep() : "save:enter", check: true, record: true);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            lock (fuzz._unhandled)
                if (fuzz._unhandled.Count > 0)
                    throw new Broken("unhandled", fuzz._unhandled[0]);
            LastLog = [.. fuzz._log];
            return null;
        }
        catch (Broken broken)
        {
            return new Failure(broken.Kind, broken.Message, [.. fuzz._done], [.. fuzz._log]);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return new Failure("exception", failure.ToString(), [.. fuzz._done], [.. fuzz._log]);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopWatching.CancelAsync();
        try
        {
            await _watching;
        }
        catch (Exception)
        {
            // Told already.
        }

        if (_session is not null)
        {
            try
            {
                if (_panel is not null) await _panel.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (Exception)
            {
                // A run that broke.
            }

            try
            {
                await _session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (Exception)
            {
                // A run that broke.
            }
        }

        _stopWatching.Dispose();
    }

    // ── Starting ────────────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<(TestSite Site, string Folder)> SharedAsync()
    {
        await Shared.WaitAsync();
        try
        {
            _site ??= TestSite.Start();
            _folder ??= Directory.CreateTempSubdirectory("qaas-state-fuzz-").FullName;
            Autosave.Folder = Path.Combine(_folder, "recordings");
            return (_site, _folder);
        }
        finally
        {
            Shared.Release();
        }
    }

    // A recording to continue: a ticket's dialog opened, its title typed, and the ticket created.
    private static async Task<Recording> ContinuedAsync(TestSite site)
    {
        await Shared.WaitAsync();
        try
        {
            if (_continued is null)
            {
                var session = await RecordingSession.StartAsync(Options($"{site.Url}/helpdesk"), _ => { });
                try
                {
                    var app = session.Page;
                    await app.GetByTestId("new-ticket").ClickAsync();
                    await app.Locator("[name='title']").FillAsync("Printer jammed");
                    await app.GetByRole(AriaRole.Button, new() { Name = "Create ticket" }).ClickAsync();
                    for (var wait = 0; wait < 50 && session.Build().Actions.Count < 3; wait++) await Task.Delay(100);
                }
                finally
                {
                    _continued = (await session.StopAsync()).ToJson();
                }
            }

            return Recording.FromJson(_continued);
        }
        finally
        {
            Shared.Release();
        }
    }

    private static RecordingOptions Options(string url) => new(url)
    {
        LoadStorageStatePath = null, SaveStorageStatePath = null, BrowserArgs = ["--headless=new"],
    };

    private string StartStep() => _random.Next(20) switch
    {
        0 or 1 => "start:login",
        2 => "start:hang",
        3 or 4 => "start:continue",
        5 => "start:/orders",
        6 => "start:/todos",
        _ => "start:/helpdesk",
    };

    private async Task StartAsync(string start)
    {
        var (site, folder) = await SharedAsync();
        var how = start["start:".Length..];
        var path = how.StartsWith('/') ? how : "/helpdesk";
        var url = site.Url + path;
        LoginFlow? login = null;
        if (how == "login")
        {
            var settings = Path.Combine(folder, "LogInFlow.flow.yaml");
            await File.WriteAllTextAsync(settings, "FlowConfiguration:\n  LogInFlow:\n    User: 'alice'\n");
            login = new LoginFlow("LogInFlow", typeof(LogInFlow).Assembly.Location, settings);
        }
        else if (how == "hang") login = new LoginFlow("HangingLogInFlow", typeof(HangingLogInFlow).Assembly.Location, null);

        var continued = how == "continue" ? await ContinuedAsync(site) : null;
        var output = Path.Combine(folder, $"run-{_seed}-{Guid.NewGuid():N}"[..24], "Flows");
        _request = new RecordRequest($"Fuzz{_seed}", url, output) { Login = login, Continue = continued is not null };
        _session = await RecordingSession.StartAsync(Options(url) with
        {
            LoginFlow = login?.Flow, LoginBaseUrl = login is null ? null : site.Url, Continues = continued,
        }, OnNews);
        foreach (var page in _session.Page.Context.Pages) Answer(page);
        _session.Page.Context.Page += (_, page) => Answer(page);
        var browser = _session.Page.Context.Browser!;
        _panel = await RecorderPanel.OpenAsync(_session, _request, new TestOptions { DefaultTimeout = 3000, SlowMotionMs = 100 });
        _view = browser.Contexts.Single(context => context != _session.Page.Context).Pages.Single();
        await WrapPanelAsync();
        // A recording continued is one the recorder kept.
        if (continued is not null) await new Autosave().WriteAsync(continued, _request);
        _autosave = new Autosave(continued, _request);
        _watching = _autosave.WatchAsync(_session.Build, () => _panel.Request, _stopWatching.Token);
        var request = _request;
        // As the recorder does: the login flow first, then the replay of the recording continued, while the person
        // may already press keys.
        _starting = Task.Run(async () =>
        {
            var failure = request.Login is not null ? await FlowRecorder.LogInAsync(_session, request) : null;
            return continued is null ? FlowRecorder.Replayed.All
                // The console's question, answered as a person does with Enter: keep recording.
                : await FlowRecorder.ReplayAsync(_session, _panel, request, failure, (_, _) => 0);
        });
        Log($"start {how} on {url}");
    }

    // The person answers a browser dialog after a moment, as the recorder keeps it open for them.
    private static void Answer(IPage page) => page.Dialog += (_, dialog) => _ = Task.Run(async () =>
    {
        await Task.Delay(100);
        try
        {
            await dialog.AcceptAsync();
        }
        catch (PlaywrightException)
        {
            // Answered already, or the page closed.
        }
    });

    private void OnNews(object news)
    {
        if (news is not RecordingState state) return;
        lock (_gate) (_announced, _announcements) = (state, _announcements + 1);
    }

    // The panel's page keeps what it was last shown, for the checks to read.
    private async Task WrapPanelAsync() => await _view.EvaluateAsync("""
        () => {
          if (window.__fuzzWrapped || typeof window.__qaasPanelShow !== 'function') return;
          const show = window.__qaasPanelShow;
          window.__qaasPanelShow = (json) => { window.__fuzzState = json; return show(json); };
          window.__fuzzWrapped = true;
          window.__qaasPanelSend?.(JSON.stringify({ type: 'ready' }));
        }
        """);

    // ── Making up the next step ─────────────────────────────────────────────────────────────────────────────────

    private async Task<string> NextAsync()
    {
        var shown = await PanelAsync();
        var actions = shown?["steps"]?.AsArray().SelectMany(step => step!["actions"]!.AsArray()).ToList() ?? [];
        var fast = _random.Next(8) == 0 ? "!" : "";
        var roll = _random.Next(100);
        if (roll < 38) return await PageStepAsync() ?? "key:F8";
        if (roll < 58)
            return fast + Pick("key:F8", "key:F8", "key:F6", "key:F6", "key:F7", "key:F7", "key:F4", "key:F9", "key:F2", "key:Escape",
                "key:Control+Shift+C", "key:Alt+ArrowUp", "key:Enter");
        if (roll < 72)
            return fast + Pick("panel:F8", "panel:F7", "panel:F6", "panel:F5", "panel:F5", "panel:Shift+F5", "panel:Control+Enter",
                "panel:Control+Shift+Enter", "panel:Control+z", "panel:Control+z", "panel:Delete", "panel:Escape", "panel:F2",
                "panel:ArrowUp", "panel:Control+Shift+C");
        if (roll < 88 && actions.Count > 0)
        {
            var at = _random.Next(actions.Count);
            return Pick($"edit:value@{at}={Word()}", $"edit:name@{at}={Word()}", $"edit:reset@{at}", $"edit:unique@{at}",
                $"edit:relative@{at}", $"edit:drop@{at}", $"edit:step@{at}={Word()}", "edit:autochecks", $"edit:choose@{at}",
                $"edit:rerecord@{at}", $"edit:after@{at}", $"edit:whole@{at}", "edit:tried", "edit:from", "edit:suggest",
                $"edit:flow={Word()}");
        }

        if (roll < 94) return Pick("wait", "sleep:500", "sleep:1500", "edit:login-by-hand");
        return Pick("goto:/helpdesk", "goto:/orders", "goto:/todos", "goto:/order", "tab:/helpdesk", "tab:/order", "tab:close",
            "tab:switch", "back", "confirm", "popup");
    }

    private string SaveStep() => _done.LastOrDefault(step => step.StartsWith("save:", StringComparison.Ordinal)) ?? Pick(
        "save:ctrl-s", "save:ctrl-s", "save:ctrl-s", "save:enter", "save:close", "save:ctrl-c", "save:ctrl-s", "save:enter",
        "save:now-ctrl-s", "save:now-enter", "save:now-close");

    private string Pick(params string[] steps) => steps[_random.Next(steps.Length)];

    private string Word() => Pick("Printer jammed", "Scanner offline", "VPN", "Laptop", "renamed", "Report", "x",
        DateTime.Today.AddDays(_random.Next(1, 20)).ToString("MM/dd/yyyy", CultureInfo.InvariantCulture), "Ticket 42", "");

    // An action in the app on what the page shows now.
    private async Task<string?> PageStepAsync()
    {
        if (CurrentPage() is not { } page) return null;
        List<Candidate> candidates;
        try
        {
            candidates = await FindAsync(page, null, null);
        }
        catch (PlaywrightException)
        {
            return null;
        }

        if (candidates.Count == 0) return null;
        var candidate = candidates[_random.Next(candidates.Count)];
        return candidate.Kind switch
        {
            "fill" => $"fill:{candidate.Name}={Word()}",
            "select" => $"select:{candidate.Name}={candidate.Options?[_random.Next(candidate.Options.Count)]}",
            _ => _random.Next(10) == 0 ? $"press:{Pick("Enter", "Escape", "Tab")}" : $"click:{candidate.Name}",
        };
    }

    private static async Task<List<Candidate>> FindAsync(IPage page, string? name, string? kind) =>
        (await page.EvaluateAsync<JsonElement>(Find, new { name, kind })).Deserialize<List<Candidate>>(Recording.Json) ?? [];

    private sealed class Candidate
    {
        public string Kind { get; set; } = "";
        public string Name { get; set; } = "";
        public List<string>? Options { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public bool Control { get; set; }
    }

    // What a person can act on in the page, by kind and name; with a name, where the first such is, scrolled into view.
    private const string Find = """
        ({ name, kind }) => {
          const shown = (element) => {
            const box = element.getBoundingClientRect();
            const style = getComputedStyle(element);
            return box.width > 0 && box.height > 0 && style.visibility !== 'hidden' && style.display !== 'none';
          };
          const nameOf = (element) => (element.getAttribute('aria-label') || element.labels?.[0]?.textContent
            || element.getAttribute('placeholder') || element.innerText || element.getAttribute('name') || element.id || '')
            .replace(/\s+/g, ' ').trim().slice(0, 40);
          const kindOf = (element) => {
            const tag = element.tagName.toLowerCase();
            if (tag === 'select') return 'select';
            if (tag === 'textarea' || tag === 'input' && !['radio', 'checkbox', 'button', 'submit', 'file', 'hidden'].includes(element.type)) return 'fill';
            return 'click';
          };
          const found = [];
          for (const element of document.querySelectorAll('button, a[href], [role=button], [role=menuitem], [role=option], [role=tab], input, select, textarea, td, h1, h2, li, label')) {
            if (!shown(element) || element.disabled) continue;
            const text = nameOf(element);
            if (!text || (name !== null && (text !== name || kindOf(element) !== kind))) continue;
            const control = element.matches('button, a[href], [role=button], [role=menuitem], input, select, textarea');
            if (name !== null) {
              element.scrollIntoView({ block: 'center', inline: 'center' });
              const box = element.getBoundingClientRect();
              return [{ kind: kindOf(element), name: text, x: box.left + box.width / 2, y: box.top + box.height / 2, control }];
            }
            found.push({ kind: kindOf(element), name: text, control,
              options: element.tagName === 'SELECT' ? [...element.options].map((option) => option.label) : undefined });
          }
          return found;
        }
        """;

    // ── Doing a step ────────────────────────────────────────────────────────────────────────────────────────────

    private IPage? CurrentPage() => _session.PageNamed(_current) ?? (_session.Page.IsClosed ? null : _session.Page);

    private async Task DoAsync(string step, bool check, bool record = false)
    {
        var plain = step.TrimStart('!');
        var (verb, argument) = plain.IndexOf(':') is >= 0 and var colon ? (plain[..colon], plain[(colon + 1)..]) : (plain, "");
        var stateBefore = _session.State;
        var checkingBefore = _session.Checking;
        int announcementsBefore;
        lock (_gate) announcementsBefore = _announcements;
        var idsBefore = Ids();
        var codeBefore = Code(_session.Build());
        var versionBefore = _session.Log.Version;
        var partBefore = check ? PartOf(await PanelAsync()) : default;
        // Check mode is the page's: a click is no action on the page that says Check.
        var checkedHere = checkingBefore && verb == "click" && (await PillAsync(CurrentPage()))?.StartsWith("Check", StringComparison.Ordinal) == true;
        var acted = false;
        if (record) _done.Add(step);
        Log($"{_done.Count - 1,3} {step}   [{stateBefore}{(checkingBefore ? " checking" : "")}]");
        try
        {
            acted = await ActAsync(verb, argument);
        }
        catch (Exception failure) when (failure is PlaywrightException or TimeoutException)
        {
            Log($"    not done: {failure.Message.Split('\n')[0]}");
        }

        if (_ended || !check) return;
        await Task.Delay(verb is "key" or "panel" ? 250 : 150);
        if (verb == "edit") await UntilAsync(() => _session.Log.Version != versionBefore, 1000);
        await AgreeAsync();
        int announcementsAfter;
        lock (_gate) announcementsAfter = _announcements;
        var steady = announcementsAfter == announcementsBefore && _session.State == stateBefore && _session.Checking == checkingBefore;

        // Nothing is recorded while paused, tested, tried or logging in; a click in check mode is no action.
        if (acted && verb is "click" or "fill" or "select" or "press" && steady)
        {
            await Task.Delay(300);
            var added = Ids().Except(idsBefore).ToList();
            if (stateBefore is not RecordingState.Recording && added.Count > 0)
                throw new Broken("recorded-while-" + stateBefore.ToString().ToLowerInvariant(),
                    $"{step} was recorded while {stateBefore}: {string.Join(", ", added)}");
            if (checkedHere && added.Any(id => KindOf(id) != "expect"))
                throw new Broken("recorded-while-checking", $"{step} was recorded in check mode: {string.Join(", ", added)}");
        }

        if (verb == "panel" && argument == "Control+z" && _undoTo is { } before)
        {
            var undone = await UntilAsync(() => Code(_session.Build()) == before, 2000);
            if (!undone) throw new Broken("undo", $"Undo after {_done[^2]} did not bring the flow back:\n{Diff(before, Code(_session.Build()))}");
        }

        var codeAfter = Code(_session.Build());
        _undoTo = verb == "edit" && codeAfter != codeBefore && UndoesAs(argument) ? codeBefore : null;
        await AutosaveAgreesAsync();
        await DonePausesAsync(verb, argument, partBefore);

        // A part done is undone whole: Undo right after Done brings the flow back as it was before the part, unless the
        // person changed something else in the panel meanwhile.
        var phase = PartOf(await PanelAsync()).Phase;
        if (verb == "edit" && argument.Split('@')[0] is "rerecord" or "after" or "whole" && partBefore.Phase is null or "done" && phase == "replaying")
            (_partStart, _editedInPart) = (codeBefore, false);
        else if (verb == "edit") _editedInPart = true;
        if (partBefore.Phase == "recording" && phase == "done" && !_editedInPart && _partStart is { } start) _undoTo = start;
        if (phase is null) _partStart = null;
    }

    // A part recorded again and done, not at the end of the flow, pauses: the app is where it left it. While trying or
    // testing, it pauses once back from them.
    private async Task DonePausesAsync(string verb, string argument, (string? Phase, bool AtEnd) before)
    {
        var resumed = verb is "key" or "panel" && argument == "F8";
        if (before.Phase == "recording") await UntilAsync(() => PartOf(PanelAsync().GetAwaiter().GetResult()).Phase != "recording", 1000);
        var now = PartOf(await PanelAsync());
        if (before.Phase == "recording" && now.Phase == "done" && !before.AtEnd && !resumed)
        {
            if (_session.State == RecordingState.Recording)
                throw new Broken("done-not-paused", "A part done in the middle of the flow goes on recording, at the flow's end");
            if (_session.State is RecordingState.Trying or RecordingState.Testing) _mustPause = true;
        }
        else if (_mustPause && (resumed || _session.State == RecordingState.Paused || now.Phase != "done")) _mustPause = false;
        else if (_mustPause && _session.State == RecordingState.Recording)
            throw new Broken("done-not-paused", "A part done in the middle of the flow, while trying or testing, records at the flow's end after it");
    }

    // The phase of the part recorded again, and whether what is recorded in it goes at the end of the flow.
    private static (string? Phase, bool AtEnd) PartOf(JsonNode? shown)
    {
        if (shown?["rerecord"] is not { } part) return (null, false);
        var actions = shown["steps"]!.AsArray().SelectMany(step => step!["actions"]!.AsArray()).Select(action => action!["id"]!.GetValue<string>()).ToList();
        var fresh = part["fresh"]?.AsArray().Select(id => id!.GetValue<string>()).ToList() ?? [];
        var at = part["at"]?.GetValue<string>();
        var atEnd = fresh.Count > 0 ? actions.Count > 0 && fresh.Contains(actions[^1]) : at is null ? actions.Count == 0 : actions.Count > 0 && at == actions[^1];
        return (part["phase"]?.GetValue<string>(), atEnd);
    }

    // Edits that Undo takes back by themselves; a part recorded again ends first.
    private static bool UndoesAs(string edit) =>
        !edit.StartsWith("rerecord", StringComparison.Ordinal) && !edit.StartsWith("after", StringComparison.Ordinal)
        && !edit.StartsWith("whole", StringComparison.Ordinal) && !edit.StartsWith("from", StringComparison.Ordinal)
        && !edit.StartsWith("flow", StringComparison.Ordinal) && !edit.StartsWith("login", StringComparison.Ordinal);

    private async Task<bool> ActAsync(string verb, string argument)
    {
        var page = CurrentPage();
        switch (verb)
        {
            case "click" or "fill" or "select" when page is not null:
            {
                var (name, value) = argument.IndexOf('=') is >= 0 and var equals && verb != "click"
                    ? (argument[..equals], argument[(equals + 1)..])
                    : (argument, "");
                var found = await FindAsync(page, name, verb);
                if (found is not [var at, ..]) return false;
                if (verb == "click")
                {
                    await page.Mouse.MoveAsync((float)at.X, (float)at.Y, new() { Steps = 2 });
                    await page.Mouse.ClickAsync((float)at.X, (float)at.Y);
                    return at.Control;
                }

                await page.Mouse.ClickAsync((float)at.X, (float)at.Y);
                var element = await page.EvaluateHandleAsync("({ x, y }) => document.elementFromPoint(x, y)", new { x = at.X, y = at.Y });
                if (element.AsElement() is not { } field) return false;
                if (verb == "fill") await field.FillAsync(value, new() { Timeout = 2000 });
                else await field.SelectOptionAsync(new SelectOptionValue { Label = value }, new() { Timeout = 2000 });
                return true;
            }
            case "press" when page is not null:
                await page.Keyboard.PressAsync(argument);
                return true;
            case "key" when page is not null:
                await page.Keyboard.PressAsync(argument);
                return false;
            case "panel":
                if (!_view.IsClosed) await _view.Keyboard.PressAsync(argument);
                return false;
            case "edit":
                await EditAsync(argument);
                return false;
            case "wait":
                await QuietAsync(TimeSpan.FromSeconds(60));
                return false;
            case "sleep":
                await Task.Delay(int.Parse(argument, CultureInfo.InvariantCulture));
                return false;
            case "goto" when page is not null:
                await page.GotoAsync(Site + argument, new() { Timeout = 10_000 });
                return false;
            case "back" when page is not null:
                await page.GoBackAsync(new() { Timeout = 10_000 });
                // Not back to the blank page the window opened with, which the recorder never shows.
                if (page.Url is "about:blank") await page.GoForwardAsync(new() { Timeout = 10_000 });
                return false;
            case "tab" when argument == "close":
                if (_current != "page" && page is not null && !page.IsClosed) await page.CloseAsync();
                _current = "page";
                return false;
            case "tab" when argument == "switch":
            {
                var aliases = new[] { "page", "page1", "page2", "page3" }.Where(alias => _session.PageNamed(alias) is not null).ToList();
                _current = aliases[(aliases.IndexOf(_current) + 1) % aliases.Count];
                await CurrentPage()!.BringToFrontAsync();
                return false;
            }
            case "tab":
            {
                var opened = await _session.Page.Context.NewPageAsync();
                await opened.GotoAsync(Site + argument, new() { Timeout = 10_000 });
                for (var alias = 1; alias < 8; alias++)
                    if (_session.PageNamed($"page{alias}") == opened)
                        _current = $"page{alias}";
                return false;
            }
            case "popup" when page is not null:
            {
                // A link that opens its page in a pop-up, as a person clicks it.
                await page.EvaluateAsync("""
                    () => { if (!document.getElementById('fuzz-popup')) document.body.insertAdjacentHTML('beforeend',
                      '<a id="fuzz-popup" href="/todos" target="_blank">Open the tasks</a>'); }
                    """);
                var before = page.Context.Pages.Count;
                var box = await page.Locator("#fuzz-popup").BoundingBoxAsync();
                if (box is null) return false;
                await page.Mouse.ClickAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
                await UntilAsync(() => page.Context.Pages.Count > before, 3000);
                var popup = page.Context.Pages.Last();
                await popup.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new() { Timeout = 5000 });
                for (var alias = 1; alias < 8; alias++)
                    if (_session.PageNamed($"page{alias}") == popup)
                        _current = $"page{alias}";
                return true;
            }
            case "code":
                // What the flow says now, for a sequence to check it.
                if (!Code(_session.Build()).Contains(argument, StringComparison.Ordinal))
                    throw new Broken("code", $"The flow does not say {argument}:\n{Code(_session.Build())}");
                return false;
            case "confirm" when page is not null:
                await page.EvaluateAsync("""
                    () => { if (!document.getElementById('fuzz-confirm')) document.body.insertAdjacentHTML('beforeend',
                      '<button id="fuzz-confirm" onclick="this.textContent = confirm(\'Sure?\') ? \'Confirmed\' : \'Not confirmed\'">Confirm</button>'); }
                    """);
                return false;
            case "save":
                await SaveAsync(argument);
                return false;
        }

        return false;
    }

    private string Site => new Uri(_session.Options.Url).GetLeftPart(UriPartial.Authority);

    // A command of the panel, as its page sends it, only when the panel offers it.
    private async Task EditAsync(string edit)
    {
        var shown = await PanelAsync();
        if (shown is null) return;
        var status = shown["status"]?.GetValue<string>();
        if (status == "saving") return;
        var part = shown["rerecord"];
        var test = shown["test"];
        var testing = test?["state"]?.GetValue<string>() is "building" or "window" or "start" or "login" or "running";
        var steps = shown["steps"]!.AsArray();
        var actions = steps.SelectMany(step => step!["actions"]!.AsArray()).ToList();
        var (what, value) = edit.IndexOf('=') is >= 0 and var equals ? (edit[..equals], edit[(equals + 1)..]) : (edit, null);
        var (kind, index) = what.IndexOf('@') is >= 0 and var at
            ? (what[..at], int.Parse(what[(at + 1)..], CultureInfo.InvariantCulture))
            : (what, 0);
        var action = actions.Count > 0 ? actions[index % actions.Count] : null;
        var id = action?["id"]?.GetValue<string>();
        var setting = action?["settings"]?.AsArray().FirstOrDefault();
        var canTest = actions.Count > 0 && !testing && status is not ("login" or "testing");
        var canRerecord = canTest && (part is null || part["phase"]?.GetValue<string>() == "done");
        object? command = kind switch
        {
            "value" when setting is not null => new { type = "editValue", action = id, role = setting["role"]?.GetValue<string>(), value },
            "name" when setting?["renamable"]?.GetValue<bool>() == true => new { type = "editValue", action = id, role = setting["role"]?.GetValue<string>(), name = value },
            "reset" when setting?["recorded"] is not null => new { type = "editValue", action = id, role = setting["role"]?.GetValue<string>(), reset = true },
            "unique" when setting?["unique"] is { } unique => new { type = "editValue", action = id, role = setting["role"]?.GetValue<string>(), unique = !unique.GetValue<bool>() },
            "relative" when setting?["suggest"] is not null || setting?["relative"] is not null =>
                new { type = "editValue", action = id, role = setting["role"]?.GetValue<string>(), relative = setting["relative"] is null },
            "drop" when action?["expects"]?.AsArray().FirstOrDefault(expect => expect!["auto"]!.GetValue<bool>()) is { } expect =>
                new { type = "dropCheck", action = id, key = expect["key"]!.GetValue<string>(), drop = expect["dropped"]?.GetValue<bool>() != true },
            "step" when id is not null => new { type = "renameStep", action = id, name = value },
            "autochecks" => new { type = "autoChecks", on = shown["autoChecks"]?.GetValue<bool>() == false },
            "choose" when action?["candidates"]?.AsArray().Where(candidate => candidate!["usable"]?.GetValue<bool>() == true).ToList() is [_, ..] usable =>
                new { type = "choose", action = id, candidate = usable[index % usable.Count]!["index"]!.GetValue<int>() },
            "rerecord" or "after" or "whole" when id is not null && canRerecord =>
                new { type = "rerecord", action = id, step = kind == "whole", after = kind == "after", label = kind == "whole" ? StepOf(steps, id) : $"the action {index}" },
            "tried" when shown["tried"]?.AsArray().LastOrDefault() is { } tried =>
                new { type = "addTried", action = tried["id"]!.GetValue<string>() },
            "from" when part?["phase"]?.GetValue<string>() == "failed" && part["failedAt"] is not null => new { type = "rerecordFrom" },
            "suggest" when actions.SelectMany(each => each!["suggestions"]?.AsArray() ?? []).FirstOrDefault() is { } suggestion =>
                new { type = "applySuggestion", key = suggestion["key"]!.GetValue<string>() },
            "flow" when value is not null => new { type = "rename", name = value },
            "login-by-hand" when status == "login" => new { type = "loginByHand" },
            _ => null,
        };
        if (command is null)
        {
            Log("    not offered");
            return;
        }

        // Not waited for, as the panel never does: the browser's other driver answers it too, with an error.
        await _view.EvaluateAsync("json => { window.__qaasPanelSend(json); }", JsonSerializer.Serialize(command));
    }

    private static string StepOf(JsonArray steps, string id) =>
        steps.First(step => step!["actions"]!.AsArray().Any(action => action!["id"]!.GetValue<string>() == id))!["name"]!.GetValue<string>();

    // ── Ending ──────────────────────────────────────────────────────────────────────────────────────────────────

    // Saved once everything settled and every mode was left; or, "now-", at once, in whatever the recorder is doing.
    private async Task SaveAsync(string how)
    {
        var now = how.StartsWith("now-", StringComparison.Ordinal);
        how = now ? how["now-".Length..] : how;
        if (!now)
        {
            // First, nothing silently stuck: once what runs ended, a mode the person sees, and leaves.
            await QuietAsync(TimeSpan.FromSeconds(90));
            await AgreeAsync();
            await NotStuckAsync();
            if (await _starting == FlowRecorder.Replayed.Stopped)
            {
                _ended = true;
                return;
            }

            await LeaveModesAsync();
            await RecordsAsync();
            await AutosaveAgreesAsync();
        }

        if (!now) await AgreeAsync();
        var shownCode = now ? null : (await PanelAsync())?["code"]?.GetValue<string>();
        var before = _session.Build();
        Recording recording;
        var request = _panel.Request;
        switch (how)
        {
            case "ctrl-s":
                var offered = (await PanelAsync())?["steps"]?.AsArray().Any(step => step!["actions"]!.AsArray().Count > 0) == true;
                await _view.Keyboard.PressAsync("Control+s");
                if (!await UntilAsync(() => _panel.SaveRequested.IsCompleted, 5000) && offered)
                    throw new Broken("save", "Ctrl+S in the panel, which shows steps, did not save");
                break;
            case "close":
                foreach (var page in _session.Page.Context.Pages.ToList()) await page.CloseAsync();
                await _session.Closed.WaitAsync(TimeSpan.FromSeconds(10));
                break;
            case "ctrl-c":
                // As a signal from outside: the recording kept as it is, then gone; the next start saves it.
                await _autosave.WriteAsync(_session.Build(), request);
                _ended = true;
                await _stopWatching.CancelAsync();
                await _watching;
                await _panel.DisposeAsync();
                await _session.DisposeAsync();
                var kept = Autosave.NotSaved().FirstOrDefault(found => found.Recording.Autosave?.Flow == request.ClassName
                                                                       && found.Recording.Autosave.Folder == Path.GetFullPath(request.OutputDir));
                if (before.Actions.Count == 0) return;
                if (kept is null) throw new Broken("ctrl-c", "the recording was not kept for the next start");
                await SavedBuildsAsync(kept.Recording, request, Code(Autosave.WithoutSecrets(before), request));
                File.Delete(kept.Path);
                return;
        }

        _ended = true;
        // As the recorder does: the recording ends once its login flow, and the replay of the recording continued, did.
        if (!await UntilAsync(() => _starting.IsCompleted, 20_000))
            throw new Broken("stuck-saving", $"Saved while {_session.State}: the recording did not end in 20 s");
        if (await _starting == FlowRecorder.Replayed.Stopped) return;
        await _panel.DisposeAsync();
        request = _panel.Request;
        recording = await _session.StopAsync();
        await _stopWatching.CancelAsync();
        await _watching;
        if (recording.Actions.Count == 0)
        {
            if (before.Actions.Count > 0)
                throw new Broken("saved-changed", $"Nothing was saved, and the recording had {before.Actions.Count} actions when it was saved");
            _autosave.Forget();
            return;
        }

        await _autosave.WriteAsync(recording, request);
        var expected = Code(before, request);
        if (shownCode is not null && Code(recording, request) != shownCode)
            throw new Broken("saved-not-shown", $"The flow saved is not the one the panel showed:\n{Diff(shownCode, Code(recording, request))}");
        await SavedBuildsAsync(recording, request, expected);
    }

    private async Task SavedBuildsAsync(Recording recording, RecordRequest request, string expected)
    {
        var flow = FlowCodeGenerator.Generate(recording, request.ClassName, ProjectNamespace.Of(request.OutputDir), request.AutoChecks);
        if (flow.Code != expected) throw new Broken("saved-changed", $"The flow saved is not the recording when it was saved:\n{Diff(expected, flow.Code)}");
        var saved = FlowRecorder.Save(request, flow);
        await _autosave.SavedAsync(recording, request, saved, flow.Code);
        using var build = new FlowBuild();
        BuiltFlow built;
        try
        {
            built = await build.BuildAsync(flow, CancellationToken.None);
        }
        catch (FlowBuildException failure)
        {
            throw new Broken("build", $"{failure.Message}\n{failure.Detail}\n{flow.Code}");
        }

        if (built.Warnings.Count > 0) throw new Broken("build", $"{string.Join("\n", built.Warnings)}\n{flow.Code}");
        lock (Saved) Saved.Add(flow);
        Log($"saved {request.ClassName}: {flow.ActionLines.Count} actions");
    }

    // Until no test, replay or login runs, or for at most so long; the hanging login is stopped by hand.
    private async Task QuietAsync(TimeSpan most)
    {
        var until = DateTime.UtcNow + most;
        var byHand = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (DateTime.UtcNow < until)
        {
            var running = _panel.Tester.Report is { Phase: not TestPhase.Done } || _session.State == RecordingState.LoggingIn || !_starting.IsCompleted;
            if (!running) return;
            if (_session.State == RecordingState.LoggingIn && DateTime.UtcNow > byHand) _session.LogInByHand();
            await Task.Delay(100);
        }

        throw new Broken("stuck", $"Still running after {most.TotalSeconds} s: {_session.State}, test {_panel.Tester.Report?.Phase}");
    }

    // A state the person sees, and a way out of it.
    private async Task NotStuckAsync()
    {
        await Task.Delay(400);
        var shown = await PanelAsync();
        var phase = shown?["rerecord"]?["phase"]?.GetValue<string>();
        var state = _session.State;
        if (state == RecordingState.Testing && _panel.Tester.Report is not { Phase: not TestPhase.Done } && _starting.IsCompleted)
            throw new Broken("stuck", $"Testing with nothing running (part {phase ?? "none"})");
        if (phase == "replaying" && _panel.Tester.Report is not { Phase: not TestPhase.Done })
            throw new Broken("stuck", "The part's replay ended, and the panel still says it replays");
    }

    // Out of every mode, as the person would: the key each says leaves it.
    private async Task LeaveModesAsync()
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var shown = await PanelAsync();
            var phase = shown?["rerecord"]?["phase"]?.GetValue<string>();
            var page = CurrentPage() ?? _session.Page;
            var pill = await PillAsync(page);
            if (pill?.StartsWith("Inspect", StringComparison.Ordinal) == true) await page.Keyboard.PressAsync("Escape");
            else if (_session.Checking) await page.Keyboard.PressAsync("F6");
            else if (phase == "failed") await _view.Keyboard.PressAsync("Escape");
            else if (_session.State == RecordingState.Trying) await page.Keyboard.PressAsync("F7");
            else if (_session.State == RecordingState.Paused) await page.Keyboard.PressAsync("F8");
            else if (phase == "recording") await page.Keyboard.PressAsync("F2");
            else if (_session.State == RecordingState.Recording) return;
            else throw new Broken("stuck", $"No way out of {_session.State}");
            Log($"    left a mode: {_session.State}, part {phase ?? "none"}, pill {pill ?? "none"}");
            await Task.Delay(400);
            await AgreeAsync();
        }

        throw new Broken("stuck", $"Still {_session.State} after pressing what leaves it");
    }

    // Recording records: a click on a button of the page is an action.
    private async Task RecordsAsync()
    {
        var page = CurrentPage() ?? _session.Page;
        await page.EvaluateAsync("""
            () => { if (!document.getElementById('fuzz-probe')) document.body.insertAdjacentHTML('beforeend',
              '<button id="fuzz-probe" onclick="this.textContent = \'Probed\'">Probe</button>'); }
            """);
        var before = Ids();
        var box = await page.Locator("#fuzz-probe").BoundingBoxAsync();
        if (box is null) return;
        await page.Mouse.ClickAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        if (!await UntilAsync(() => Ids().Except(before).Any(), 3000))
            throw new Broken("not-recording", $"{_session.State} everywhere, and a click on a button was not recorded");
    }

    // ── Checks ──────────────────────────────────────────────────────────────────────────────────────────────────

    // The model, the console, the panel and each page's overlay say one state, soon.
    private async Task AgreeAsync()
    {
        string? problem = null;
        for (var waited = 0; waited < Agreement; waited += 100)
        {
            problem = await DisagreementAsync();
            if (problem is null) return;
            await Task.Delay(100);
        }

        throw new Broken("disagree:" + problem!.Split(' ')[0], problem);
    }

    private async Task<string?> DisagreementAsync()
    {
        var state = _session.State;
        var checking = _session.Checking;
        RecordingState? announced;
        lock (_gate) announced = _announced;
        if (announced is { } said && said != state)
            return $"console says {said}, the recorder is {state}";
        var shown = await PanelAsync();
        if (shown is null) return "panel shows nothing";
        var status = shown["status"]?.GetValue<string>();
        var expected = state switch
        {
            RecordingState.Paused => "paused", RecordingState.LoggingIn => "login", RecordingState.Testing => "testing",
            RecordingState.Trying => "trying", _ => "recording",
        };
        if (status != expected) return $"panel says {status}, the recorder is {state}";
        // The panel shows the flow exactly as it will be saved.
        if (shown["code"]?.GetValue<string>() is { } code && code != Code(_session.Build()) && shown["error"] is null)
            return $"panel code is not the flow as it will be saved:\n{Diff(Code(_session.Build()), code)}";
        if ((shown["checking"]?.GetValue<bool>() ?? false) != checking) return $"panel says checking {!checking}, the recorder {checking}";
        var part = shown["rerecord"];
        var phase = part?["phase"]?.GetValue<string>();
        if (phase is "replaying" && state != RecordingState.Testing) return $"panel says replaying, the recorder is {state}";
        if (shown["tried"] is not null && state != RecordingState.Trying && !(state == RecordingState.Testing && _session.Tried is not null))
            return $"panel shows tried actions, the recorder is {state}";
        if (await StripAsync() is var (name, label) && !StripFits(name, label, state, checking))
            return $"strip says {name}: {label}, the recorder is {state}{(checking ? " checking" : "")}";
        foreach (var alias in new[] { "page", "page1", "page2", "page3", "page4" })
        {
            if (_session.PageNamed(alias) is not { } page) continue;
            var pill = await PillAsync(page);
            if (pill == "?") continue;
            var part2 = _session.RerecordingPart;
            var fits = pill switch
            {
                _ when pill?.StartsWith("Inspect", StringComparison.Ordinal) == true => true,
                _ when pill?.StartsWith("Not now", StringComparison.Ordinal) == true || pill?.EndsWith(", then check", StringComparison.Ordinal) == true
                       || pill?.StartsWith("Trying · record again first", StringComparison.Ordinal) == true => false,
                _ when pill?.StartsWith("Check", StringComparison.Ordinal) == true => checking,
                _ => state switch
                {
                    RecordingState.Paused => pill?.StartsWith("Paused", StringComparison.Ordinal) == true,
                    RecordingState.Testing => pill?.StartsWith("Testing the flow", StringComparison.Ordinal) == true,
                    RecordingState.Trying => pill?.StartsWith("Trying", StringComparison.Ordinal) == true,
                    RecordingState.LoggingIn => pill is null || pill.StartsWith("Logging in", StringComparison.Ordinal),
                    _ => part2 is null ? pill is null : pill?.StartsWith(part2, StringComparison.Ordinal) == true,
                },
            };
            if (!fits) return $"overlay of {alias} says {pill ?? "nothing"}, the recorder is {state}{(checking ? " checking" : "")}{(part2 is null ? "" : $" ({part2})")}";
        }

        return null;
    }

    // What the panel's status strip says, as the person reads it: its name and its label.
    private async Task<(string Name, string Label)?> StripAsync()
    {
        try
        {
            var strip = await _view.EvaluateAsync<string[]?>("""
                () => {
                  const names = ['Saving', 'Logging in', 'Replaying', 'Re-recording', 'Test run', 'Check mode', 'Inspect', 'Trying', 'Testing', 'Paused', 'Recording'];
                  const strip = [...document.querySelectorAll('[role=status][aria-label], [role=alert][aria-label]')].find((element) => names.includes(element.getAttribute('aria-label')));
                  return strip ? [strip.getAttribute('aria-label'), strip.querySelector('span')?.textContent ?? ''] : null;
                }
                """);
            return strip is [var name, var label] ? (name, label) : null;
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    private static bool StripFits(string name, string label, RecordingState state, bool checking) => state switch
    {
        _ when name == "Inspect" => state is not (RecordingState.Testing or RecordingState.LoggingIn),
        RecordingState.Paused => label == "Paused",
        RecordingState.Trying => label == "Trying",
        RecordingState.LoggingIn => name == "Logging in",
        RecordingState.Testing => name is "Testing" or "Test run" or "Re-recording" or "Replaying" && label is not ("Paused" or "Trying"),
        _ when checking => name == "Check mode",
        _ => name is "Recording" or "Re-recording" or "Test run" && label is not ("Paused" or "Trying" or "Testing"),
    };

    // The recording kept on disk is the recording, as the next start would load it.
    private async Task AutosaveAgreesAsync()
    {
        string? problem = null;
        for (var waited = 0; waited < 3000; waited += 100)
        {
            var built = _session.Build();
            var request = _panel.Request;
            var kept = Autosave.Find(request.OutputDir, request.ClassName);
            if (built.Actions.Count == 0)
            {
                // Nothing left of the recording: none is offered when the recorder starts next.
                problem = _request.Continue || kept is null || !kept.Path.EndsWith(".unsaved.json", StringComparison.Ordinal) || kept.Recording.Actions.Count == 0
                    ? null
                    : $"the recording has no action, and its autosave {kept.Recording.Actions.Count}";
                if (problem is null) return;
                await Task.Delay(100);
                continue;
            }

            if (kept is null) problem = "no autosave";
            else
            {
                var expected = Code(Autosave.WithoutSecrets(built), request);
                var reloaded = new CaptureLog(from: kept.Recording).Build(built.StartUrl);
                var again = Code(reloaded with { AutoChecks = kept.Recording.AutoChecks, After = kept.Recording.After }, request);
                problem = again == expected ? null : Diff(expected, again);
            }

            if (problem is null) return;
            await Task.Delay(100);
        }

        throw new Broken("autosave", $"The autosave does not reload to the recording:\n{problem}");
    }

    private async Task<JsonNode?> PanelAsync()
    {
        try
        {
            if (_view.IsClosed) return null;
            var json = await _view.EvaluateAsync<string?>("() => { if (!window.__fuzzWrapped) return null; return window.__fuzzState ?? null; }");
            if (json is null) await WrapPanelAsync();
            return json is null ? null : JsonNode.Parse(json);
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    // What the pill at the top of the page says, or null when it shows none; "?" when it cannot be read now.
    private static async Task<string?> PillAsync(IPage? page)
    {
        if (page is null || page.IsClosed || page.Url is "" or "about:blank") return "?";
        ICDPSession devTools;
        try
        {
            devTools = await page.Context.NewCDPSessionAsync(page);
        }
        catch (PlaywrightException)
        {
            return "?";
        }

        try
        {
            var document = await devTools.SendAsync("DOM.getDocument", new() { ["depth"] = -1, ["pierce"] = true });
            return PillIn(document!.Value.GetProperty("root"), inOverlay: false) is { } pill
                ? pill.Shown ? pill.Text : null
                : null;
        }
        catch (PlaywrightException)
        {
            return "?";
        }
        finally
        {
            try
            {
                await devTools.DetachAsync();
            }
            catch (PlaywrightException)
            {
                // The page closed.
            }
        }
    }

    private static (bool Shown, string Text)? PillIn(JsonElement node, bool inOverlay)
    {
        inOverlay |= node.GetProperty("nodeName").GetString() == "QAAS-RECORDER-OVERLAY";
        if (inOverlay && node.TryGetProperty("attributes", out var attributes))
        {
            var pairs = attributes.EnumerateArray().Select(item => item.GetString() ?? "").ToList();
            var at = pairs.IndexOf("class");
            if (at >= 0 && at % 2 == 0 && pairs[at + 1].Split(' ').Contains("pill"))
                return (pairs[at + 1].Split(' ').Contains("shown"), string.Join(" · ", TextsIn(node)));
        }

        foreach (var kind in (string[])["children", "shadowRoots"])
            if (node.TryGetProperty(kind, out var children))
                foreach (var child in children.EnumerateArray())
                    if (PillIn(child, inOverlay) is { } found)
                        return found;
        return null;
    }

    private static IEnumerable<string> TextsIn(JsonElement node)
    {
        if (node.GetProperty("nodeType").GetInt32() == 3) yield return node.GetProperty("nodeValue").GetString()!;
        if (node.TryGetProperty("children", out var children))
            foreach (var text in children.EnumerateArray().SelectMany(TextsIn))
                yield return text;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────────────────

    private HashSet<string> Ids() => [.. _session.Log.Build("").Actions.Select(action => action.Id)];

    private string? KindOf(string id) => _session.Log.Build("").Actions.FirstOrDefault(action => action.Id == id)?.Kind;

    private string Code(Recording recording, RecordRequest? request = null)
    {
        request ??= _panel?.Request ?? _request;
        try
        {
            return FlowCodeGenerator.Generate(recording, request.ClassName, ProjectNamespace.Of(request.OutputDir), request.AutoChecks).Code;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            throw new Broken("generate", $"The flow cannot be written: {failure}");
        }
    }

    private static string Diff(string expected, string actual)
    {
        var (was, now) = (expected.Split('\n'), actual.Split('\n'));
        var text = new StringBuilder();
        foreach (var line in was.Except(now).Take(12)) text.AppendLine($"  - {line}");
        foreach (var line in now.Except(was).Take(12)) text.AppendLine($"  + {line}");
        return text.Length > 0 ? text.ToString() : $"  (the same lines, in another order: {was.Length} and {now.Length})";
    }

    private static async Task<bool> UntilAsync(Func<bool> condition, int ms)
    {
        for (var waited = 0; waited < ms && !condition(); waited += 50) await Task.Delay(50);
        return condition();
    }

    private void Log(string line)
    {
        lock (_log) _log.Add($"{DateTime.Now:HH:mm:ss.fff} {line}");
    }
}
