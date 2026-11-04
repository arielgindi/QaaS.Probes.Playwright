using System.Text.Json;
using System.Threading.Channels;
using QaaS.Playwright.Files;
using QaaS.Playwright.Recorder.Login;
using QaaS.Playwright.Recorder.Panel;
using QaaS.Playwright.Recorder.State;

namespace QaaS.Playwright.Recorder.Setup;

/// <summary>
/// The window's start screen: what to record is set up here, field by field, each checked by <see cref="SetupRules"/>
/// as it changes, against what was found in the project in the background (its flows, the recordings never saved, and
/// the login search); Start hands the request on, and once the recording ended the screen shows what was saved. The
/// screen has the recorder's state machine for its own states (the start screen, a build of the test project, a start,
/// what was saved), and every command the page sends is carried out on one worker, in the order it came.
/// </summary>
internal sealed class SetupScreen
{
    private readonly PanelHost _host;
    private readonly Channel<Func<Task>> _work = Channel.CreateUnbounded<Func<Task>>(new() { SingleReader = true });
    private readonly RecorderStateMachine _machine = new(new NoDrain(), new NoEffects(), RecorderState.StartScreen);
    private readonly SemaphoreSlim _searching = new(1, 1);
    private readonly Dictionary<string, string> _typed = new(StringComparer.OrdinalIgnoreCase);
    private TaskCompletionSource<RecordRequest?> _next = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SetupDraft _draft = new();
    private SetupFacts _facts = new();
    private string _found = "";
    private LoginLook? _look;
    private string? _loginFlow;
    private string? _loginSettings;
    private int _generation;
    private RunScope? _build;
    private string? _buildLog;
    private IReadOnlyList<WhyLine>? _why;
    private BrowseView? _browse;
    private DoneView? _done;
    private string? _notice;
    private string? _lastFlow;
    private bool _starting;
    private string? _toldLogin;

    public SetupScreen(PanelHost host, string startFolder)
    {
        _host = host;
        host.Route(Receive);
        _ = host.Closed.ContinueWith(_ => _next.TrySetResult(null), TaskScheduler.Default);
        _ = Task.Run(RunAsync);
        var project = Folders.ProjectIn(startFolder);
        var found = project is null ? null : "found from where you started";
        if (project is null && ProjectSettings.Recent().FirstOrDefault(recent => Directory.Exists(recent.Folder)) is { } last)
            (project, found) = (last.Folder, "the project you recorded in last");
        Post(async () =>
        {
            if (project is not null) Open(project, found!);
            await RenderAsync();
        });
    }

    /// <summary>The recorder's state as the start screen has it: on it, building, starting, or showing what was saved.</summary>
    public RecorderState State => _machine.State;

    /// <summary>The project the screen is on, and how it was found; null before one is chosen.</summary>
    public (string? Folder, string Found) Project => (_draft.Project, _found);

    /// <summary>Completes with the request to record once Start was pressed; with null once the window was closed, or Close.</summary>
    public Task<RecordRequest?> NextAsync() => _next.Task;

    /// <summary>Ends the start screen at once, as Ctrl+C there does.</summary>
    public void Quit() => Post(() => QuitAsync(new Stop(StopReason.CtrlC)));

    /// <summary>
    /// The recording ended as <paramref name="outcome"/> says: the window shows what was saved, or, for a recording that
    /// did not start, the start screen again with why.
    /// </summary>
    public Task EndedAsync(RecordingOutcome outcome)
    {
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(async () =>
        {
            _host.Route(Receive);
            _starting = false;
            _next = new TaskCompletionSource<RecordRequest?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!outcome.Started)
            {
                await _machine.RequestAsync(new StartFailed(outcome.Error ?? "The recording did not start."));
                _notice = outcome.Error;
                Rediscover();
            }
            else
            {
                await _machine.RequestAsync(new Stop(StopReason.Saved));
                await ShowDoneAsync(outcome);
            }

            await RenderAsync();
            shown.TrySetResult();
        });
        return shown.Task;
    }

    // On Playwright's thread: only queued, for the worker.
    private void Receive(string json)
    {
        JsonElement command;
        try
        {
            command = JsonDocument.Parse(json).RootElement.Clone();
        }
        catch (JsonException)
        {
            return;
        }

        Post(() => CarryAsync(command));
    }

    private void Post(Func<Task> work) => _work.Writer.TryWrite(work);

    private async Task RunAsync()
    {
        await foreach (var work in _work.Reader.ReadAllAsync())
        {
            try
            {
                await work();
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                CrashReport.Log("The start screen's command failed", failure);
                _notice = $"That did not work ({failure.GetType().Name}: {failure.Message}), a bug of the recorder. What happened is in {CrashReport.LogPath}.";
                await RenderAsync();
            }
        }
    }

    private async Task CarryAsync(JsonElement command)
    {
        var type = Text(command, "type");
        var path = Text(command, "path");
        _notice = null;
        switch (type)
        {
            case "setup":
                Set(Text(command, "field") ?? "", command.TryGetProperty("value", out var value) ? value : default);
                break;
            case "start":
                await StartAsync();
                break;
            case "build":
                await BuildAsync();
                break;
            case "stopBuild":
                await _machine.RequestAsync(new StopRun());
                if (_build is { } build) await build.DisposeAsync();
                break;
            case "refresh":
                Rediscover();
                break;
            case "why" when command.TryGetProperty("on", out var on) && on.ValueKind == JsonValueKind.True && _draft.Project is { } project:
                var folder = OutputFolder;
                _why = [new WhyLine(0, "Looking…")];
                _ = Task.Run(() =>
                {
                    var lines = LoginFinder.Explained(folder, project).Lines.Select(line => new WhyLine(line.Depth, line.Text)).ToList();
                    Post(async () =>
                    {
                        if (_why is not null) _why = lines;
                        await RenderAsync();
                    });
                });
                break;
            case "why":
                _why = null;
                break;
            case "browse":
                _browse = Folders.Browse(path ?? (_browse?.Path ?? _draft.Project ?? Folders.Here), Text(command, "mode") ?? "folder",
                    ProjectSettings.Recent().Select(recent => recent.Folder));
                break;
            case "browseClose":
                _browse = null;
                break;
            case "use" when path is not null:
                _browse = null;
                if (Text(command, "mode") == "yaml") Set("loginSettings", JsonDocument.Parse(JsonSerializer.Serialize(path)).RootElement);
                else Open(path, "chosen");
                break;
            case "forget" when path is not null || Text(command, "folder") is not null:
                ProjectSettings.Forget(Text(command, "folder") ?? path!);
                break;
            case "recovered" when path is not null:
                await RecoveredAsync(path, Text(command, "act"));
                break;
            case "again" or "continueSaved" when _machine.State.Mode == Mode.Done:
                await _machine.RequestAsync(new NewRecording());
                _done = null;
                _draft = type == "continueSaved" && _lastFlow is not null
                    ? _draft with { Tab = SetupTab.Continue, ContinueFlow = _lastFlow, Name = "", IfExists = IfExists.Unset }
                    : _draft with { Tab = SetupTab.New, Name = "", IfExists = IfExists.Unset, ContinueFlow = null, Recovered = null };
                Rediscover();
                break;
            case "quit":
                await QuitAsync(new Quit());
                return;
            case "openFolder" when _done?.Files.FirstOrDefault() is { } file:
                OpenFolder(Path.GetDirectoryName(Path.GetFullPath(file.Path, _draft.Project ?? Folders.Here))!);
                break;
            case "prefs":
                _ = _host.Prefs.SetAsync(Text(command, "name"), Text(command, "value"));
                return;
        }

        await RenderAsync();
    }

    // A field the person changed, as the page sends it.
    private void Set(string field, JsonElement value)
    {
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        var on = value.ValueKind == JsonValueKind.True;
        switch (field)
        {
            case "project":
                Open(text, "chosen");
                break;
            case "outputDir":
                _draft = _draft with { OutputDir = text.Trim() };
                Rediscover();
                break;
            case "tab":
                _draft = _draft with { Tab = Enum.TryParse<SetupTab>(text, ignoreCase: true, out var tab) ? tab : SetupTab.New };
                break;
            case "name":
                if (!string.Equals(SetupRules.NameProblem(text) is null ? SetupRules.ClassName(text) : text, ClassName, StringComparison.OrdinalIgnoreCase))
                    _draft = _draft with { IfExists = IfExists.Unset };
                _draft = _draft with { Name = text };
                break;
            case "url":
                _draft = _draft with { Url = text.Trim() };
                break;
            case "ifExists":
                _draft = _draft with { IfExists = Enum.TryParse<IfExists>(text, ignoreCase: true, out var choice) ? choice : IfExists.Unset };
                break;
            case "continueFlow":
                _draft = _draft with { ContinueFlow = text };
                break;
            case "recovered":
                _draft = _draft with { Recovered = text };
                break;
            case "logIn":
                _draft = _draft with { LogIn = on };
                break;
            case "autoChecks":
                _draft = _draft with { AutoChecks = on };
                break;
            case "keepRecording":
                _draft = _draft with { KeepRecording = on };
                break;
            case "loginFlow":
                _loginFlow = text;
                _typed.Clear();
                ApplyLogin();
                break;
            case "loginProject" when _draft.Project is { } project && text.Length > 0:
                RememberedLogin.RememberProject(project, text);
                Rediscover();
                break;
            case "loginSettings":
                _loginSettings = text;
                ApplyLogin();
                break;
            case var named when named.StartsWith("value:", StringComparison.Ordinal):
                _typed[named["value:".Length..]] = text;
                ApplyLogin();
                break;
        }
    }

    private string? ClassName => SetupRules.NameProblem(_draft.Name) is null ? SetupRules.ClassName(_draft.Name) : null;

    private string OutputFolder => Path.GetFullPath(Path.Combine(_draft.Project ?? Folders.Here,
        string.IsNullOrWhiteSpace(_draft.OutputDir) ? SetupRules.DefaultOutputDir : _draft.OutputDir));

    // The project chosen, with what was chosen in it before.
    private void Open(string folder, string found)
    {
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var choices = ProjectSettings.Of(folder);
        _draft = new SetupDraft
        {
            Project = folder, OutputDir = choices?.OutputDir ?? SetupRules.DefaultOutputDir, Tab = choices?.Tab == SetupTab.Continue ? SetupTab.Continue : SetupTab.New,
            Url = choices?.Urls.FirstOrDefault() ?? "", LogIn = RememberedLogin.DeclinedIn(folder) is null,
            AutoChecks = choices?.AutoChecks ?? true, KeepRecording = choices?.KeepRecording ?? false,
        };
        (_found, _loginFlow, _loginSettings, _look, _toldLogin) = (found, null, choices?.LoginSettings, null, null);
        _typed.Clear();
        ConsoleUi.Info($"Project   {folder}  ({found})");
        Rediscover();
    }

    // What is in the project now: its flows and the recordings never saved at once, its login in the background.
    private void Rediscover()
    {
        if (_draft.Project is not { } project)
        {
            _facts = new SetupFacts { NotSaved = Autosave.NotSaved() };
            return;
        }

        var outputFolder = OutputFolder;
        var notSaved = Autosave.NotSaved();
        _facts = new SetupFacts
        {
            ProjectExists = Directory.Exists(project), Flows = ProjectFlows.Of(outputFolder), NotSaved = notSaved, LookingForLogin = true,
            Login = _facts.Login,
        };
        if (notSaved.Count > 0 && _draft.Tab == SetupTab.New && _draft.Name.Length == 0 && _draft.Recovered is null)
            _draft = _draft with { Tab = SetupTab.Recovered, Recovered = notSaved[0].Path };
        var generation = ++_generation;
        _ = Task.Run(async () =>
        {
            await _searching.WaitAsync();
            LoginLook? look = null;
            try
            {
                if (generation == _generation && Directory.Exists(project)) look = LoginFinder.Look(outputFolder, project);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                CrashReport.Log("The login search failed", failure);
            }
            finally
            {
                _searching.Release();
            }

            Post(async () =>
            {
                if (generation != _generation) return;
                _look = look;
                _facts = _facts with { LookingForLogin = false };
                ApplyLogin();
                await RenderAsync();
            });
        });
    }

    // The login to run first, as found or chosen, with the values typed for it over its file's.
    private void ApplyLogin()
    {
        var (flow, discovered) = ChosenLogin();
        if (flow is null || discovered is null)
        {
            _facts = _facts with { Login = null };
            return;
        }

        if (_typed.Count == 0 && _draft.Project is { } project && ProjectSettings.Of(project)?.Values.GetValueOrDefault(flow.Name) is { } kept)
            foreach (var (name, value) in kept) _typed[name] = value;
        LoginSettings.TypeValues(flow.Name, _typed);
        var login = LoginFinder.LoginOf(flow, discovered);
        if (_loginSettings is { } settings) login = login with { Settings = settings.Length == 0 ? null : settings };
        _facts = _facts with { Login = FlowCheck.CannotRun(flow.Type) is null ? login : null };
        var told = $"{flow.Name}, from {Rel(login.Assembly)}" + (login.Settings is { } file ? $"; its values from {Rel(file)}" : "");
        if (told != _toldLogin) ConsoleUi.Info($"Login     {told}");
        _toldLogin = told;
    }

    private (FoundFlow? Flow, Discovered? Discovered) ChosenLogin()
    {
        if (_look?.Discovered is not { } discovered) return (null, null);
        var flow = _loginFlow is { Length: > 0 } chosen
            ? discovered.Flows.FirstOrDefault(each => string.Equals(each.Name, chosen, StringComparison.OrdinalIgnoreCase))
            : _look.Suggested;
        return (flow, discovered);
    }

    private async Task StartAsync()
    {
        var check = SetupRules.Check(_draft, _facts);
        if (check.Request is not { } request || _machine.State is not { Mode: Mode.Setup, Run: null })
        {
            _notice = check.Problems.Values.FirstOrDefault() ?? "Not now: wait for what goes on to end.";
            return;
        }

        Remember(request);
        await _machine.RequestAsync(new Start());
        _starting = true;
        _lastFlow = request.ClassName;
        await RenderAsync();
        _next.TrySetResult(request);
    }

    // What was chosen here, for next time: the page first among those kept, the login or a no to it, the values typed
    // that are not secrets.
    private void Remember(RecordRequest request)
    {
        var project = _draft.Project!;
        var was = ProjectSettings.Of(project);
        var values = new Dictionary<string, IReadOnlyDictionary<string, string>>(was?.Values ?? new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            StringComparer.OrdinalIgnoreCase);
        if (ChosenLogin().Flow is { } flow)
            values[flow.Name] = _typed.Where(pair => pair.Value.Length > 0 && !Configuration.Secrets.IsSecretKey(pair.Key)).ToDictionary();
        var notRemembered = ProjectSettings.Remember(new ProjectChoices
        {
            Folder = project, OutputDir = _draft.OutputDir, Urls = was?.Urls ?? [], LoginSettings = _loginSettings, Values = values,
            AutoChecks = _draft.AutoChecks, KeepRecording = _draft.KeepRecording, Tab = _draft.Tab,
        }, _draft.Tab == SetupTab.New && !request.Continue ? request.Url : null);
        notRemembered ??= request.Login is { } login ? RememberedLogin.Remember(project, login)
            : !_draft.LogIn && _look?.Suggested is { } declined ? RememberedLogin.Decline(project, declined.Name)
            : null;
        if (notRemembered is not null) ConsoleUi.Warning(notRemembered);
    }

    private async Task BuildAsync()
    {
        if (_look?.Discovered is not { Project: { } project } discovered || !(await _machine.RequestAsync(new BuildProject())).Accepted) return;
        var run = _machine.State.Run!;
        var scope = _build = new RunScope("Build");
        _buildLog = null;
        ConsoleUi.Phase($"Building {project.Name}");
        _ = scope.Run(async stop =>
        {
            var (exitCode, log) = await LoginFinder.BuildAsync(project, discovered.Build, stop);
            Post(async () =>
            {
                if (_build != scope) return;
                _build = null;
                await scope.DisposeAsync();
                if (exitCode != 0 && !stop.IsCancellationRequested)
                {
                    _buildLog = string.Join('\n', log.Split('\n').Where(line => line.Contains("error", StringComparison.OrdinalIgnoreCase)).Take(20));
                    ConsoleUi.BuildFailed(project.Name, log, null);
                }

                await _machine.RequestAsync(new RunEnded(run.Id, exitCode == 0 ? RunOutcome.Passed : RunOutcome.Failed));
                Rediscover();
                await RenderAsync();
            });
        });
    }

    private async Task RecoveredAsync(string path, string? act)
    {
        var kept = _facts.NotSaved.FirstOrDefault(each => each.Path == path);
        if (kept is null) return;
        switch (act)
        {
            case "save" when (await _machine.RequestAsync(new SaveRecovered())).Accepted:
                var outcome = new RecordingOutcome();
                await FlowRecorder.SaveKeptAsync(kept, outcome);
                await ShowDoneAsync(outcome);
                break;
            case "continue":
                _draft = _draft with { Tab = SetupTab.Recovered, Recovered = path };
                await StartAsync();
                break;
            case "discard":
                FileStore.Machine.Delete(path);
                ConsoleUi.Info($"Discarded the unsaved recording of {kept.Recording.Autosave?.Flow}; nothing else changed.");
                _draft = _draft with { Recovered = null, Tab = _draft.Tab == SetupTab.Recovered ? SetupTab.New : _draft.Tab };
                Rediscover();
                break;
        }
    }

    private async Task ShowDoneAsync(RecordingOutcome outcome)
    {
        await _machine.RequestAsync(new Ended());
        _lastFlow = outcome.Flow;
        _done = new DoneView(outcome.Saved, outcome.Flow, outcome.Saved ? outcome.Summary : "Nothing was written.",
            [.. outcome.Files.Select(file => new DoneFile(_draft.Project is { } project ? Path.GetRelativePath(project, file.Path) : file.Path, file.What))],
            outcome.Notes, outcome.Checks, outcome.Error);
    }

    private async Task QuitAsync(Command command)
    {
        await _machine.RequestAsync(command);
        if (_build is { } build) await build.DisposeAsync();
        _next.TrySetResult(null);
    }

    private static void OpenFolder(string folder)
    {
        try
        {
            var opener = OperatingSystem.IsWindows() ? "explorer" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(opener, [folder]) { UseShellExecute = false });
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ConsoleUi.Info($"The flow is in {folder}.");
        }
    }

    private Task RenderAsync() => _host.CallAsync("__qaasSetupShow", JsonSerializer.Serialize(View(), Panel.PanelView.Json));

    private SetupView View()
    {
        var check = SetupRules.Check(_draft, _facts);
        var project = _draft.Project;
        var outputFolder = OutputFolder;
        var className = ClassName;
        var recent = ProjectSettings.Recent();
        var urls = (ProjectSettings.Of(project ?? "")?.Urls ?? []).Select(url => new UrlView(url, "recent"))
            .Concat((_look?.Discovered?.Files ?? []).SelectMany(file => LoginSettings.BaseUrls(file.Yaml).Select(url => new UrlView(url, Rel(file.Path)))))
            .DistinctBy(url => url.Url).Take(8).ToList();
        var ready = _machine.State is { Mode: Mode.Setup, Run: null } && !_starting;
        return new SetupView(
            _machine.State.Mode == Mode.Done ? "done" : "setup", RecorderView.Of(_machine.State), ConsoleUi.Version,
            project is null ? null : new ProjectView(project, _look?.Discovered?.Project?.Name ?? Path.GetFileName(project), _found,
                _facts.ProjectExists ? ProjectNamespace.Of(outputFolder) : null, !_facts.ProjectExists),
            [.. recent.Select(each => new RecentView(each.Folder, Path.GetFileName(each.Folder), ConsoleUi.Ago(each.LastUsed), !Directory.Exists(each.Folder)))],
            new DraftView(_draft.Tab.ToString().ToLowerInvariant(), _draft.Name, _draft.Url, _draft.IfExists.ToString().ToLowerInvariant(),
                _draft.ContinueFlow, _draft.Recovered, _draft.LogIn, _draft.AutoChecks, _draft.KeepRecording, _draft.OutputDir),
            className is null || project is null ? null : new NameView(className, Path.GetRelativePath(project, Path.Combine(outputFolder, $"{className}.cs"))),
            check.Problems.ContainsKey("url") ? SetupRules.WithScheme(_draft.Url) : null, urls,
            [.. _facts.Flows.Select(FlowViewOf)],
            [.. _facts.NotSaved.Select(kept => new RecoveredView(kept.Path, kept.Recording.Autosave!.Flow,
                $"{ConsoleUi.Count(kept.Recording.Actions.Count, "action")} · {ConsoleUi.Ago(kept.Changed)} · {Rel(kept.Recording.Autosave.Folder)}",
                project is not null && kept.Recording.Autosave.Folder.StartsWith(project, StringComparison.Ordinal)))],
            LoginViewOf(), check.Problems, check.WillDo, check.StartLabel, ready && check.Request is not null, _starting, _notice, _why, _browse, _done);
    }

    private static FlowView FlowViewOf(ProjectFlow flow) => flow.Kept is { } kept
        ? new FlowView(flow.ClassName, $"{ConsoleUi.Count(kept.Recording.Actions.Count, "action")} · recorded {ConsoleUi.Ago(kept.Changed)} · " +
                                       $"{PathOf(kept.Recording.StartUrl)}{(kept.Recording.After is { } after ? $" · after {after}" : "")}", true, null)
        : new FlowView(flow.ClassName, "", false, "No recording kept: it was recorded on another machine, with an older recorder, or before the last 30 saved.");

    private static string PathOf(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;

    private LoginView LoginViewOf()
    {
        var building = _machine.State.Run is { Kind: RunKind.Build };
        if (_draft.Project is null)
            return new("none", null, null, null, [], [], null, [], [], null, null, false, null, [], "Choose a project first.");
        if (_facts.LookingForLogin && _look is null)
            return new("looking", null, $"Looking for your login flow in {Path.GetFileName(_draft.Project)}…", null, [], [], null, [], [], null, null, building, _buildLog, [], null);
        var look = _look;
        var projects = look?.Projects.Select(project => new LoginProjectView(project.ProjectFile, project.Name)).ToList() ?? [];
        var discovered = look?.Discovered;
        var flows = discovered is null ? [] : discovered.Likely.Concat(discovered.Flows.Except(discovered.Likely))
            .Select(flow => new LoginChoiceView(flow.Name, flow.SetupIn > 0 ? $"the setup flow of {ConsoleUi.Count(flow.SetupIn, "test file")}" : Path.GetFileName(flow.Dll))).ToList();
        var problems = discovered?.Problems.ToList() ?? [];
        var (flow, _) = ChosenLogin();
        if (projects.Count > 0)
            return new("choose", null, null, null, flows, projects, null, [], [], null, null, building, _buildLog, problems,
                $"{projects.Count} test projects here use QaaS: choose the one whose login flow the recorder should run.");
        if (flow is null || discovered is null)
            return new("none", null, null, null, flows, [], null, [], [], look?.Stale, look?.BuildCommand, building, _buildLog, problems,
                $"No login flow: {look?.Search.Why ?? "none of your tests' flows logs in"}. The recording starts signed out.");
        if (FlowCheck.CannotRun(flow.Type) is { } cannot && !problems.Contains(cannot)) problems.Add(cannot);
        var login = LoginFinder.LoginOf(flow, discovered);
        if (_loginSettings is { } chosen) login = login with { Settings = chosen.Length == 0 ? null : chosen };
        var files = LoginFinder.SettingsFilesOf(discovered, flow.Name).ToList();
        if (login.Settings is { } settings && !files.Contains(settings)) files.Insert(0, settings);
        var values = Values(flow, login.Settings, problems);
        var (baseUrl, from) = login.BaseUrlFor(_draft.Url);
        var why = discovered.Again == flow ? "you logged in with it here before"
            : flow.SetupIn > 0 ? $"the setup flow of {ConsoleUi.Count(flow.SetupIn, "test file")}"
            : "its name says it logs in";
        var shownBaseUrl = from is not null ? $"{baseUrl} (from {Rel(from)})"
            : _draft.Url.Length > 0 ? $"{baseUrl} (the start page's address)" : null;
        return new("found", flow.Name, $"{Rel(flow.Dll)} · {why}", shownBaseUrl, flows, [], login.Settings ?? "",
            [.. files.Select(file => new LoginProjectView(file, Rel(file)))],
            values, look?.Stale, look?.BuildCommand, building, _buildLog, problems, null);
    }

    // The login's values: those given and where from, those missing; a secret's never shown.
    private List<LoginValueView> Values(FoundFlow flow, string? settings, List<string> problems)
    {
        LoginValues values;
        Microsoft.Extensions.Configuration.IConfiguration? read = null;
        try
        {
            values = LoginValues.Of(flow.Type, settings);
            read = LoginSettings.Read(settings, flow.Name);
        }
        catch (InvalidOperationException unreadable)
        {
            problems.Add(unreadable.Message);
            return [];
        }

        problems.AddRange(values.Problems);
        var shown = new List<LoginValueView>();
        foreach (var name in values.Given.Concat(values.Missing).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var secret = Configuration.Secrets.IsSecretKey(name);
            var typed = _typed.GetValueOrDefault(name) is { Length: > 0 } text ? text : null;
            var missing = values.Missing.Contains(name);
            var from = typed is not null ? "typed here" : missing ? "set nowhere" : settings is null ? "its default" : Rel(settings);
            shown.Add(new LoginValueView(name, from, missing, secret, secret ? "" : typed ?? read?[$"{flow.Name}:{name}"] ?? ""));
        }

        return shown;
    }

    // A path as the person knows it: under the project, from it; else whole.
    private string Rel(string path)
    {
        if (_draft.Project is not { } project) return path;
        var relative = Path.GetRelativePath(project, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? path : relative;
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed class NoEffects : IEffects
    {
        public Task ApplyAsync(Effect effect, RecorderState before, RecorderState after) => Task.CompletedTask;
    }
}
