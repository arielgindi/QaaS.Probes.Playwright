using System.Text;
using System.Text.RegularExpressions;
using QaaS.Playwright.Recorder.Login;

namespace QaaS.Playwright.Recorder;

/// <summary>What to record: the flow's class name, the page to start on, and the folder to save the flow in.</summary>
internal sealed partial record RecordRequest(string ClassName, string Url, string OutputDir)
{
    /// <summary>Save the raw recording beside the flow, with every locator tried for each element.</summary>
    public bool KeepRecording { get; init; }

    private const string DefaultOutputDir = "Flows";

    // A file name has at most 255 bytes, and a re-recording adds ".recorded-<n>.txt" to the flow's name.
    private const int MaxNameBytes = 200;

    // Types a flow's code uses, which a flow of the same name would hide.
    private static readonly string[] TypesAFlowUses =
        ["Task", "IPage", "BasePlaywrightFlow", "Expect", "AriaRole", "Regex", "IDialog", "Console", "Array"];

    /// <summary>Whether the recorder's panel shows the flow beside the recorded window as it is written.</summary>
    public bool Panel { get; init; } = true;

    /// <summary>Whether the flow checks what each action did when recorded (<c>--no-auto-checks</c> turns it off).</summary>
    public bool AutoChecks { get; init; } = true;

    /// <summary>A flow of the person's tests that logs in before the recording starts; null to start at once.</summary>
    public LoginFlow? Login { get; init; }

    /// <summary>The login flow is the one last used in this folder, not given in the command.</summary>
    public bool LoginRemembered { get; init; }

    /// <summary>The person said to record without logging in (<c>--no-login</c>, or none when asked): none is looked for.</summary>
    public bool NoLogin { get; init; }

    /// <summary>Goes on from the flow's recording: its steps replay first, and what is recorded now comes after them.</summary>
    public bool Continue { get; init; }

    /// <summary>
    /// The flow file saving replaces, by its <see cref="Autosave.Hash"/>: the flow continued, or the one the person chose
    /// to overwrite. When the file changed since, or this is null, saving keeps it, as it keeps any other.
    /// </summary>
    public string? Replaces { get; init; }

    /// <summary>The flow's class file.</summary>
    public string CodePath => Path.Combine(OutputDir, $"{ClassName}.cs");

    /// <summary>
    /// Parses <c>record &lt;name&gt; &lt;url&gt; [--output-dir Dir] [--keep-recording] [--no-panel] [--no-auto-checks]
    /// [--login Flow [--login-assembly Shared.dll] [--login-settings Flow.flow.yaml] | --no-login]</c>, with the options
    /// anywhere, or <c>--continue &lt;name or .cs&gt;</c> with the same options instead of a name and a URL. What the login
    /// options leave out comes from <paramref name="find"/>, which finds a flow by its name in the test project of the
    /// output folder, then from <paramref name="remembered"/>, the login last used here; a login found in the tests,
    /// rather than given, is found again instead.
    /// </summary>
    /// <exception cref="ArgumentException">The arguments are not a valid record command.</exception>
    public static RecordRequest Parse(IReadOnlyList<string> args, LoginFlow? remembered = null,
        Func<string, string, LoginFlow?>? find = null)
    {
        var continuing = args is ["--continue", ..];
        if (args is not ["record", ..] && !continuing) throw new ArgumentException($"Unknown command '{string.Join(' ', args)}'.");

        var outputDir = DefaultOutputDir;
        var keepRecording = false;
        var panel = true;
        var autoChecks = true;
        var noLogin = false;
        string? loginFlow = null, loginAssembly = null, loginSettings = null, continued = null;
        var positionals = new List<string>();
        for (var index = continuing ? 0 : 1; index < args.Count; index++)
        {
            string ValueOf(string option, string what) =>
                index + 1 < args.Count && args[index + 1] is var value && value.Trim().Length > 0 && !value.StartsWith('-')
                    ? args[++index]
                    : throw new ArgumentException($"{option} needs {what}.");

            switch (args[index])
            {
                case "--output-dir": outputDir = ValueOf("--output-dir", "a directory"); break;
                case "--keep-recording": keepRecording = true; break;
                case "--no-panel": panel = false; break;
                case "--no-auto-checks": autoChecks = false; break;
                case "--login": loginFlow = ValueOf("--login", "the login flow's class name"); break;
                case "--login-assembly":
                    loginAssembly = ValueOf("--login-assembly", "the DLL in your test project's bin folder that has the flow");
                    break;
                case "--login-settings": loginSettings = ValueOf("--login-settings", "a YAML file"); break;
                case "--no-login": noLogin = true; break;
                case "--continue": continued = ValueOf("--continue", "the flow's name, or its .cs file"); break;
                case var option when option.StartsWith('-'): throw new ArgumentException($"Unknown option '{option}'.");
                default: positionals.Add(args[index]); break;
            }
        }

        if (continued is not null && positionals.Count > 0)
            throw new ArgumentException("--continue takes the flow's name or its .cs file, without a name or a URL: the " +
                                        "flow starts where it was recorded.");
        if (continued is null && positionals is not [_, _])
            throw new ArgumentException("Expected: record <name> <url> [--output-dir Dir] [--keep-recording] [--no-panel] " +
                                        "[--no-auto-checks] [--login Flow [--login-assembly Shared.dll] " +
                                        "[--login-settings Flow.flow.yaml] | --no-login], or --continue <name> with them");
        var given = loginFlow ?? loginAssembly ?? loginSettings;
        if (noLogin && given is not null) throw new ArgumentException("--no-login cannot go with the other login options.");
        // --login alone: the flow's DLL and values as the tests have them.
        var named = loginFlow is not null && loginAssembly is null ? find?.Invoke(loginFlow, outputDir) : null;
        var login = noLogin ? null
            : given is null ? remembered is { Found: false } ? CheckedLogin(remembered.Flow, remembered.Assembly, remembered.Settings) : null
            : named is not null ? loginSettings is null ? named : CheckedLogin(named.Flow, named.Assembly, loginSettings)
            : CheckedLogin(loginFlow ?? remembered?.Flow, loginAssembly ?? remembered?.Assembly,
                loginSettings ?? (loginFlow is null || loginFlow == remembered?.Flow ? remembered?.Settings : null));
        // A flow named in the output folder, or its file; the page to start on is its recording's.
        var isFile = continued?.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) == true;
        var request = continued is null ? Create(positionals[0], positionals[1], outputDir)
            : isFile ? new RecordRequest(ClassNameOf(Path.GetFileNameWithoutExtension(continued)), "",
                Path.GetDirectoryName(continued) is { Length: > 0 } folder ? folder : ".")
            : new RecordRequest(ClassNameOf(continued), "", outputDir);
        return request with
        {
            Continue = continued is not null,
            KeepRecording = keepRecording, Panel = panel, AutoChecks = autoChecks, Login = login, NoLogin = noLogin,
            LoginRemembered = login is not null && given is null,
        };
    }

    /// <summary>
    /// Asks for the page, the flow's name and the folder, again until each answer is valid, and for a login flow to run
    /// first: the one <paramref name="find"/> finds in the tests, when the folder has no login given before, else by
    /// its name, DLL and YAML.
    /// </summary>
    /// <exception cref="OperationCanceledException">The input ended before all were given.</exception>
    public static RecordRequest Ask(LoginFlow? remembered = null, Func<RecordRequest, RecordRequest?>? find = null)
    {
        ConsoleUi.Intro("Let's record a browser flow.");
        var url = ConsoleUi.Ask("URL to start on", "", answer => answer.Length == 0
            ? "Type the address of the page to start on, e.g. https://my-app.com/login"
            : Refusal(() => EnsureHttpUrl(answer)));
        string AskName() => ConsoleUi.Ask("Flow name", "my-flow", answer => Refusal(() => Create(answer, url, DefaultOutputDir)));
        var request = Create(AskName(), url, ConsoleUi.Ask("Save in folder", DefaultOutputDir));
        // A flow of that name is there: go on from it, record it anew in its place, or name this one otherwise.
        while (File.Exists(request.CodePath))
        {
            var choice = ConsoleUi.Choose($"{request.CodePath} is there already",
                ["Continue it", "Overwrite", "Choose another name"]);
            if (choice < 2)
            {
                request = choice == 0
                    ? request with { Continue = true }
                    : request with { Replaces = Autosave.Hash(File.ReadAllText(request.CodePath)) };
                break;
            }

            request = Create(AskName(), url, request.OutputDir);
        }

        var given = remembered is { Found: false } ? remembered : null;
        if (given is null && find?.Invoke(request) is { } found) return found;
        var flow = ConsoleUi.Ask("Log in first with a flow of your tests (its class name, or none)", given?.Flow ?? "none");
        if (flow.Equals("none", StringComparison.OrdinalIgnoreCase)) return request with { NoLogin = true };
        var example = Path.Combine("bin", "Debug", "net10.0", "YourShared.dll");
        var assembly = ConsoleUi.Ask($"The DLL in your test project's bin folder that has {flow}, e.g. {example}", given?.Assembly ?? "",
            answer => Refusal(() => CheckedLogin(flow, answer, null)));
        var settings = ConsoleUi.Ask($"The YAML with its values (FlowConfiguration: {flow}:), or none if it needs none",
            given?.Settings ?? "none");
        return request with
        {
            Login = CheckedLogin(flow, assembly, settings.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : settings),
        };
    }

    private static string? Refusal(Action check)
    {
        try
        {
            check();
            return null;
        }
        catch (ArgumentException refused)
        {
            return ConsoleUi.Reason(refused);
        }
    }

    /// <summary>
    /// The same request for a flow named <paramref name="name"/>, checked as a new name is. It replaces no flow file.
    /// </summary>
    /// <exception cref="ArgumentException">The flow cannot have that name.</exception>
    public RecordRequest Renamed(string name) => this with { ClassName = ClassNameOf(name), Replaces = null };

    // Checked before Chrome opens; the paths are kept whole, so the login is remembered whatever folder it ran in.
    private static LoginFlow CheckedLogin(string? flow, string? assembly, string? settings)
    {
        if (string.IsNullOrWhiteSpace(flow)) throw new ArgumentException("--login needs the login flow's class name.");
        if (string.IsNullOrWhiteSpace(assembly))
            throw new ArgumentException($"{flow} is not in a build of the test project here. Build it (dotnet build), or " +
                                        $"give --login-assembly: the DLL in your test project's bin folder that has {flow}, " +
                                        $"e.g. {Path.Combine("bin", "Debug", "net10.0", "YourShared.dll")}.");
        if (!File.Exists(assembly))
            throw new ArgumentException($"There is no {assembly}: build your test project first (dotnet build), or " +
                                        "record with --no-login.");
        if (settings is not null && !File.Exists(settings)) throw new ArgumentException($"There is no {settings}.");
        return new LoginFlow(flow.Trim(), Path.GetFullPath(assembly), settings is null ? null : Path.GetFullPath(settings));
    }

    private static RecordRequest Create(string name, string url, string outputDir)
    {
        EnsureHttpUrl(url);
        return new RecordRequest(ClassNameOf(name), url, outputDir);
    }

    // Checked before recording, so nobody records a flow that cannot be saved or compiled.
    private static string ClassNameOf(string name)
    {
        var className = FlowCodeGenerator.ToPascalCase(name);
        var clash = TypesAFlowUses.Contains(className) ? "a type its code uses"
            : WindowsDeviceName().IsMatch(className) ? "a device on Windows"
            : null;
        if (clash is not null)
            throw new ArgumentException($"A flow cannot be named {className}, {clash}. Try {className}Flow.");
        if (Encoding.UTF8.GetByteCount(className) > MaxNameBytes)
            throw new ArgumentException($"The flow's name is too long to be a file name: '{className}'.");
        return className;
    }

    [GeneratedRegex("^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])$", RegexOptions.IgnoreCase)]
    private static partial Regex WindowsDeviceName();

    private static void EnsureHttpUrl(string url)
    {
        var isHttp = Uri.TryCreate(url, UriKind.Absolute, out var uri)
                     && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        if (!isHttp)
            throw new ArgumentException($"'{url}' is not an http(s) URL. Example: https://my-app.com/login");
    }
}
