using System.Text;
using System.Text.RegularExpressions;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Login;

namespace QaaS.Playwright.Recorder.Setup;

/// <summary>What the start screen records: a new flow, one saved to go on from, or a recording never saved.</summary>
internal enum SetupTab { New, Continue, Recovered }

/// <summary>What to do with a flow of the name given that is there already.</summary>
internal enum IfExists { Unset, Continue, Replace }

/// <summary>
/// What the person chose on the start screen, field by field, as they chose it: nothing here is checked yet. The
/// project is a folder; the output folder is relative to it.
/// </summary>
internal sealed record SetupDraft
{
    public string? Project { get; init; }
    public string OutputDir { get; init; } = SetupRules.DefaultOutputDir;
    public SetupTab Tab { get; init; } = SetupTab.New;
    public string Name { get; init; } = "";
    public string Url { get; init; } = "";
    public IfExists IfExists { get; init; }

    /// <summary>The class name of the saved flow to go on from.</summary>
    public string? ContinueFlow { get; init; }

    /// <summary>The file of the recording never saved to take up again.</summary>
    public string? Recovered { get; init; }

    /// <summary>Log in first with the tests' login flow, when one is found.</summary>
    public bool LogIn { get; init; } = true;

    public bool AutoChecks { get; init; } = true;
    public bool KeepRecording { get; init; }
}

/// <summary>A flow in the output folder: its class, its file's hash, and the recording kept to go on from, if any.</summary>
internal sealed record ProjectFlow(string ClassName, string CodePath, string Hash, KeptRecording? Kept);

/// <summary>
/// What the start screen found for the draft, which the rules check it against without reading anything themselves:
/// whether the project is there, the flows of its output folder, the recordings never saved, and the login to run
/// first when the person logs in (null when none is found, or the search is still going).
/// </summary>
internal sealed record SetupFacts
{
    public bool ProjectExists { get; init; }
    public IReadOnlyList<ProjectFlow> Flows { get; init; } = [];
    public IReadOnlyList<KeptRecording> NotSaved { get; init; } = [];
    public LoginFlow? Login { get; init; }

    /// <summary>The login search is going on: Start waits for it, unless the person turns logging in off.</summary>
    public bool LookingForLogin { get; init; }
}

/// <summary>
/// The draft, checked: what is wrong with each field, by the field's name, and the request to record once nothing is;
/// with the sentence that says what Start will do, and what its button says.
/// </summary>
internal sealed record SetupCheck(RecordRequest? Request, IReadOnlyDictionary<string, string> Problems, string WillDo, string StartLabel);

/// <summary>
/// The one set of rules a recording's setup is checked by, wherever it comes from: the start screen, as the person
/// types; the tests and the in-process API, through <see cref="For"/>; and the panel's rename of a flow. They read no
/// file: what they need is in <see cref="SetupFacts"/>.
/// </summary>
internal static partial class SetupRules
{
    public const string DefaultOutputDir = "Flows";

    // A file name has at most 255 bytes, and a re-recording adds ".recorded-<n>.txt" to the flow's name.
    private const int MaxNameBytes = 200;

    /// <summary>A request to record <paramref name="name"/> from <paramref name="url"/> into <paramref name="outputDir"/>.</summary>
    /// <exception cref="ArgumentException">The name or the address cannot be recorded.</exception>
    public static RecordRequest For(string name, string url, string outputDir = DefaultOutputDir)
    {
        CheckUrl(url);
        return new RecordRequest(ClassName(name), url, outputDir);
    }

    /// <summary>The class a flow named <paramref name="name"/> becomes: <c>new-order</c> is <c>NewOrder</c>.</summary>
    /// <exception cref="ArgumentException">A flow cannot be named so: why, and what to try.</exception>
    public static string ClassName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Give the flow a name, e.g. new-order: it becomes the class NewOrder that your tests run.");
        string className;
        try
        {
            className = FlowCodeGenerator.ToPascalCase(name);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException($"'{name.Trim()}' has no letter or digit a class name can use. Try a name such as new-order.");
        }

        var clash = Generation.CSharp.Reserved.Contains(className) ? "a type or member its code uses"
            : WindowsDeviceName().IsMatch(className) ? "a device on Windows"
            : null;
        if (clash is not null)
            throw new ArgumentException($"A flow cannot be named {className}, {clash}. Try {className}Flow.");
        if (Encoding.UTF8.GetByteCount(className) > MaxNameBytes)
            throw new ArgumentException($"The flow's name is too long to be a file name: '{className}'. Choose a shorter " +
                                        "name, e.g. new-order.");
        return className;
    }

    /// <summary>Why the recorder cannot open <paramref name="url"/>, or null when it can.</summary>
    public static string? UrlProblem(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "Type the address of the page to start on, e.g. https://shop.test/orders.";
        var isHttp = Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
                     && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && uri.Host.Length > 0;
        return isHttp ? null
            : $"'{url.Trim()}' is not an address the recorder can open: it must start with http:// or https://, e.g. " +
              "https://shop.test/orders.";
    }

    /// <summary>The address with <c>https://</c> in front, when it was typed without one and that makes it one.</summary>
    public static string? WithScheme(string url) =>
        !string.IsNullOrWhiteSpace(url) && !url.Contains("://", StringComparison.Ordinal) && UrlProblem("https://" + url.Trim()) is null
            ? "https://" + url.Trim()
            : null;

    /// <exception cref="ArgumentException">The recorder cannot open <paramref name="url"/>.</exception>
    public static void CheckUrl(string url)
    {
        if (UrlProblem(url) is { } problem) throw new ArgumentException(problem);
    }

    /// <summary>Why a flow cannot be named <paramref name="name"/>, or null when it can.</summary>
    public static string? NameProblem(string name)
    {
        try
        {
            ClassName(name);
            return null;
        }
        catch (ArgumentException refused)
        {
            return refused.Message;
        }
    }

    /// <summary>The draft checked against what was found: each field's problem, and the request once there is none.</summary>
    public static SetupCheck Check(SetupDraft draft, SetupFacts facts)
    {
        var problems = new Dictionary<string, string>();
        if (draft.Project is null) problems["project"] = "Choose the folder of your test project first: flows are saved there.";
        else if (!facts.ProjectExists) problems["project"] = $"{draft.Project} is not there any more. Choose another project.";
        if (problems.Count > 0) return new SetupCheck(null, problems, "Choose a project to record in.", "Start recording");

        var outputDir = Path.GetFullPath(Path.Combine(draft.Project!, string.IsNullOrWhiteSpace(draft.OutputDir) ? DefaultOutputDir : draft.OutputDir.Trim()));
        RecordRequest? request = null;
        string? what = null;
        switch (draft.Tab)
        {
            case SetupTab.New:
                (request, what) = NewFlow(draft, facts, outputDir, problems);
                break;
            case SetupTab.Continue:
                (request, what) = Continued(draft, facts, outputDir, problems);
                break;
            default:
                (request, what) = Recovered(draft, facts, problems);
                break;
        }

        // The login: the tests' one when the person logs in and one is found. A flow recorded after a login replays
        // after it, as found here, else as it was recorded.
        var login = draft.LogIn ? facts.Login : null;
        var kept = request?.Continue == true ? KeptOf(draft, facts) : null;
        if (draft.LogIn && facts.LookingForLogin && facts.Login is null)
            problems["login"] = "Looking for your login flow: Start waits for it, or turn Log in first off.";
        else if (kept?.After is { } after && !Same(login?.Flow, after))
        {
            if (!draft.LogIn)
                problems["login"] = $"{request!.ClassName} was recorded after the login flow {after}, so its steps would replay " +
                                    "signed out: turn Log in first on.";
            else if (Same(kept.Autosave?.Login?.Flow, after)) login = kept.Autosave!.Login;
            else
                problems["login"] = $"{request!.ClassName} was recorded after the login flow {after}, which the tests here do not " +
                                    $"have now, so its steps would replay signed out. Build your test project, or choose the " +
                                    $"project that has {after}.";
        }

        if (problems.Count > 0 || request is null)
            return new SetupCheck(null, problems, problems.Count > 0 ? "Fix what is marked, then Start." : "", StartLabel(draft, login, request));
        request = request with
        {
            // A flow gone on from keeps its auto checks, or their absence, as it was recorded.
            AutoChecks = request.Continue ? request.AutoChecks : draft.AutoChecks,
            KeepRecording = draft.KeepRecording || request.KeepRecording,
            Login = login,
            NoLogin = !draft.LogIn,
            LoginRemembered = false,
        };
        var willDo = (request.Continue ? $"Chrome opens where {request.ClassName} started" : $"Chrome opens {request.Url}")
                     + (login is null ? "" : $", logs in with {login.Flow}")
                     + (request.Continue ? $", replays its steps, then records more after them, into {Relative(request.CodePath, draft.Project!)}."
                         : $", then records {request.ClassName} into {Relative(request.CodePath, draft.Project!)}.");
        return new SetupCheck(request, problems, what ?? willDo, StartLabel(draft, login, request));
    }

    private static (RecordRequest?, string?) NewFlow(SetupDraft draft, SetupFacts facts, string outputDir, Dictionary<string, string> problems)
    {
        string? className = null;
        try
        {
            className = ClassName(draft.Name);
        }
        catch (ArgumentException refused)
        {
            problems["name"] = refused.Message;
        }

        if (UrlProblem(draft.Url) is { } wrong) problems["url"] = wrong;
        if (className is null || problems.Count > 0) return (null, null);

        var existing = facts.Flows.FirstOrDefault(flow => string.Equals(flow.ClassName, className, StringComparison.OrdinalIgnoreCase));
        var request = new RecordRequest(className, draft.Url.Trim(), outputDir);
        if (existing is null) return (request, null);
        switch (draft.IfExists)
        {
            case IfExists.Continue when existing.Kept is null:
                problems["ifExists"] = NothingKept(existing.ClassName);
                return (null, null);
            case IfExists.Continue:
                return (request with { ClassName = existing.ClassName, Url = existing.Kept.Recording.StartUrl, Continue = true }, null);
            case IfExists.Replace:
                return (request with { ClassName = existing.ClassName, Replaces = existing.Hash }, null);
            default:
                problems["ifExists"] = $"{Path.GetFileName(existing.CodePath)} is there already: continue it, replace it with a " +
                                       "new recording, or give this flow another name.";
                return (null, null);
        }
    }

    private static (RecordRequest?, string?) Continued(SetupDraft draft, SetupFacts facts, string outputDir, Dictionary<string, string> problems)
    {
        var flow = facts.Flows.FirstOrDefault(each => string.Equals(each.ClassName, draft.ContinueFlow, StringComparison.OrdinalIgnoreCase));
        if (flow is null)
        {
            problems["continue"] = facts.Flows.Count == 0
                ? "There is no flow in this folder yet: record one under New flow."
                : "Choose the flow to go on from.";
            return (null, null);
        }

        if (flow.Kept is null)
        {
            problems["continue"] = NothingKept(flow.ClassName);
            return (null, null);
        }

        var autosaved = flow.Kept.Recording.Autosave;
        return (new RecordRequest(flow.ClassName, flow.Kept.Recording.StartUrl, outputDir)
        {
            Continue = true, AutoChecks = autosaved?.AutoChecks ?? true, KeepRecording = autosaved?.KeepRecording ?? false,
        }, null);
    }

    private static (RecordRequest?, string?) Recovered(SetupDraft draft, SetupFacts facts, Dictionary<string, string> problems)
    {
        var kept = facts.NotSaved.FirstOrDefault(each => each.Path == draft.Recovered);
        if (kept?.Recording.Autosave is not { } flow)
        {
            problems["recovered"] = facts.NotSaved.Count == 0 ? "There is no recording to recover." : "Choose the recording to go on with.";
            return (null, null);
        }

        return (new RecordRequest(flow.Flow, kept.Recording.StartUrl, flow.Folder)
        {
            AutoChecks = flow.AutoChecks, Replaces = flow.Replaces, KeepRecording = flow.KeepRecording, Continue = true,
        }, null);
    }

    // The recording gone on from, which says the login flow it ran after.
    private static Recording? KeptOf(SetupDraft draft, SetupFacts facts) => draft.Tab switch
    {
        SetupTab.Recovered => facts.NotSaved.FirstOrDefault(each => each.Path == draft.Recovered)?.Recording,
        SetupTab.Continue => facts.Flows.FirstOrDefault(each => Same(each.ClassName, draft.ContinueFlow))?.Kept?.Recording,
        _ => NameProblem(draft.Name) is null ? facts.Flows.FirstOrDefault(each => Same(each.ClassName, ClassName(draft.Name)))?.Kept?.Recording : null,
    };

    private static bool Same(string? one, string? other) => string.Equals(one, other, StringComparison.OrdinalIgnoreCase);

    private static string NothingKept(string flow) =>
        $"{flow} cannot be continued: the recorder kept no recording of it, as it was recorded on another machine, with " +
        "an older recorder, or before the last 30 saved. Record it again, with Keep the raw recording on to continue it anywhere.";

    private static string StartLabel(SetupDraft draft, LoginFlow? login, RecordRequest? request) =>
        draft.Tab == SetupTab.Recovered ? "Continue recording"
        : request?.Continue == true || draft.Tab == SetupTab.Continue ? $"Continue {request?.ClassName ?? draft.ContinueFlow ?? "the flow"}"
        : login is not null ? "Log in and start"
        : "Start recording";

    private static string Relative(string path, string project)
    {
        var relative = Path.GetRelativePath(project, path);
        return relative.StartsWith("..", StringComparison.Ordinal) ? path : relative;
    }

    [GeneratedRegex("^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])$", RegexOptions.IgnoreCase)]
    private static partial Regex WindowsDeviceName();
}
