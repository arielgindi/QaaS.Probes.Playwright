using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.Playwright;
using QaaS.Playwright.Recorder.Capture;

namespace QaaS.Playwright.Recorder.Panel;

/// <summary>
/// The recorder's panel: a window beside the recorded one that shows the flow as it is written, step by step, and
/// takes the person's corrections before it is saved. It is a page of the recording's Chrome in a browser context of
/// its own, so nothing done in it is recorded, and it needs no server or port: its page is embedded in the tool, it is
/// shown each state by <see cref="IPage.EvaluateAsync"/>, and it sends its commands through an exposed function.
/// Closing it leaves the recording going.
/// </summary>
internal sealed class RecorderPanel : IAsyncDisposable
{
    private const string Resource = "QaaS.Playwright.Recorder.Panel.panel.html";

    // The page's side (Panel/ui/src/bridge.ts): it shows a state given as JSON, and sends each command as JSON.
    private const string ShowFunction = "json => typeof window.__qaasPanelShow === 'function' && window.__qaasPanelShow(json)";
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
    private Task _worker = Task.CompletedTask;

    // The worker's own: what the panel shows, and what changes it besides the recording.
    private PanelSnapshot? _shown;
    private string? _namespace;
    private IPage? _highlighted;
    private string? _error;
    private bool _saving;
    private bool _refresh = true;

    private RecorderPanel(RecordingSession session, RecordRequest request, IPage page)
    {
        _session = session;
        _page = page;
        Request = request;
    }

    /// <summary>The request, under the name the person gave the flow in the panel.</summary>
    public RecordRequest Request { get; private set; }

    /// <summary>Completes when the person clicked Save: the recording ends and is saved as when the window closes.</summary>
    public Task SaveRequested => _saveRequested.Task;

    /// <summary>Opens the panel beside the recorded window.</summary>
    /// <exception cref="PlaywrightException">The browser could not open it.</exception>
    public static async Task<RecorderPanel> OpenAsync(RecordingSession session, RecordRequest request)
    {
        var browser = session.Page.Context.Browser ?? throw new PlaywrightException("The recording has no browser.");
        // No viewport, so the page fills its window; no color scheme, so it follows the system's.
        var context = await browser.NewContextAsync(new() { ViewportSize = ViewportSize.NoViewport, ColorScheme = ColorScheme.Null });
        try
        {
            var page = await context.NewPageAsync();
            var panel = new RecorderPanel(session, request, page);
            var beside = await PanelWindow.PlaceAsync(session.Page, page);
            await page.ExposeFunctionAsync<string>(SendFunction, panel.Receive);
            await page.SetContentAsync(Html.Value);
            if (beside) await session.Page.BringToFrontAsync();
            panel._worker = Task.Run(panel.RunAsync);
            return panel;
        }
        catch
        {
            await context.CloseAsync();
            throw;
        }
    }

    /// <summary>Stops updating the panel; the window closes with the browser.</summary>
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _worker;
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
                    Carry(command);
                // A highlight replaces the one before, so only the last matters.
                if (commands.LastOrDefault(command => command.Type is "highlight" or "unhighlight") is { } highlight)
                    await HighlightAsync(highlight);

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
            case "undo" when _shown is { Undo.Count: > 0 } shown:
                _session.Log.Remove(shown.Undo);
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
                _saveRequested.TrySetResult();
                break;
        }
    }

    private async Task ShowAsync()
    {
        try
        {
            _namespace ??= ProjectNamespace.Of(Request.OutputDir);
            _shown = PanelSnapshot.Of(_session.Log.Build(Request.Url), Request, _namespace, _saving, _error);
        }
        catch (Exception failure)
        {
            // A recording the writer cannot write cannot be saved either: the panel says why, and the recording goes on.
            var last = _shown ?? PanelSnapshot.Of(new Recording(Request.Url, []), Request, _namespace ?? "Flows", _saving, null);
            _shown = last with { View = last.View with { Error = $"Could not write the flow: {failure.Message}" } };
        }

        var json = JsonSerializer.Serialize(_shown.View, PanelView.Json);
        if (await _page.EvaluateAsync<bool>(ShowFunction, json)) return;
        // The page lost the panel, e.g. it was reloaded: put it back.
        await _page.SetContentAsync(Html.Value);
        await _page.EvaluateAsync<bool>(ShowFunction, json);
    }

    // Shows in the recorded page the element of an action, or what one of its candidates finds; null hides it.
    private async Task HighlightAsync(PanelCommand? command)
    {
        var action = command is { Type: "highlight", Action: { } id } ? _shown?.Actions.GetValueOrDefault(id) : null;
        var candidate = command?.Candidate is { } index ? action?.Source.Element?.Candidates.ElementAtOrDefault(index) : action?.Locator;
        var page = action is null ? null : _session.PageNamed(action.Source.Page);
        try
        {
            if (_highlighted is { IsClosed: false } previous && previous != page) await previous.Locator(Nothing).HighlightAsync();
            _highlighted = page;
            if (page is not null)
                await ((candidate is null ? null : Locate(page, candidate)) ?? page.Locator(Nothing)).HighlightAsync();
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
    private sealed record PanelCommand(string Type, string? Action, int? Candidate, string? Name);
}
