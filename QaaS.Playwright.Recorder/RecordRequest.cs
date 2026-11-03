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

    /// <summary>A flow of the person's tests that logs in before the recording starts; null to start at once.</summary>
    public LoginFlow? Login { get; init; }

    /// <summary>The login flow is the one last used in this folder, not given in the command.</summary>
    public bool LoginRemembered { get; init; }

    /// <summary>
    /// Parses <c>record &lt;name&gt; &lt;url&gt; [--output-dir Dir] [--keep-recording] [--no-panel] [--login Flow
    /// --login-assembly Tests.dll [--login-settings Flow.flow.yaml] | --no-login]</c>, with the options anywhere. What
    /// the login options leave out comes from <paramref name="remembered"/>, the login last used here.
    /// </summary>
    /// <exception cref="ArgumentException">The arguments are not a valid record command.</exception>
    public static RecordRequest Parse(IReadOnlyList<string> args, LoginFlow? remembered = null)
    {
        if (args is not ["record", ..]) throw new ArgumentException($"Unknown command '{string.Join(' ', args)}'.");

        var outputDir = DefaultOutputDir;
        var keepRecording = false;
        var panel = true;
        var noLogin = false;
        string? loginFlow = null, loginAssembly = null, loginSettings = null;
        var positionals = new List<string>();
        for (var index = 1; index < args.Count; index++)
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
                case "--login": loginFlow = ValueOf("--login", "the login flow's class name"); break;
                case "--login-assembly": loginAssembly = ValueOf("--login-assembly", "your test project's built .dll"); break;
                case "--login-settings": loginSettings = ValueOf("--login-settings", "a YAML file"); break;
                case "--no-login": noLogin = true; break;
                case var option when option.StartsWith('-'): throw new ArgumentException($"Unknown option '{option}'.");
                default: positionals.Add(args[index]); break;
            }
        }

        if (positionals is not [var name, var url])
            throw new ArgumentException("Expected: record <name> <url> [--output-dir Dir] [--keep-recording] [--no-panel] " +
                                        "[--login Flow --login-assembly Tests.dll [--login-settings Flow.flow.yaml] | --no-login]");
        var given = loginFlow ?? loginAssembly ?? loginSettings;
        if (noLogin && given is not null) throw new ArgumentException("--no-login cannot go with the other login options.");
        var login = noLogin ? null
            : given is null ? remembered is null ? null : CheckedLogin(remembered.Flow, remembered.Assembly, remembered.Settings)
            : CheckedLogin(loginFlow ?? remembered?.Flow, loginAssembly ?? remembered?.Assembly,
                loginSettings ?? (loginFlow is null || loginFlow == remembered?.Flow ? remembered?.Settings : null));
        return Create(name, url, outputDir) with
        {
            KeepRecording = keepRecording, Panel = panel, Login = login, LoginRemembered = login is not null && given is null,
        };
    }

    /// <summary>Asks for the page, the flow's name and the folder, and for a login flow to run first.</summary>
    public static RecordRequest Ask(LoginFlow? remembered = null)
    {
        ConsoleUi.Info("Let's record a browser flow.\n");
        var url = ConsoleUi.Ask("What website do you want to record?", "");
        EnsureHttpUrl(url);
        var name = ConsoleUi.Ask("Give this flow a name", "my-flow");
        var request = Create(name, url, ConsoleUi.Ask("Where to save it?", DefaultOutputDir));
        var flow = ConsoleUi.Ask("Log in first with a flow of your tests? Its class name, or none", remembered?.Flow ?? "none");
        if (flow.Equals("none", StringComparison.OrdinalIgnoreCase)) return request;
        var assembly = ConsoleUi.Ask("Your test project's built .dll, e.g. bin/Debug/net10.0/MyTests.dll", remembered?.Assembly ?? "");
        var settings = ConsoleUi.Ask("Its settings in a YAML file, or none", remembered?.Settings ?? "none");
        return request with
        {
            Login = CheckedLogin(flow, assembly, settings.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : settings),
        };
    }

    /// <summary>The same request for a flow named <paramref name="name"/>, checked as a new name is.</summary>
    /// <exception cref="ArgumentException">The flow cannot have that name.</exception>
    public RecordRequest Renamed(string name) =>
        Create(name, Url, OutputDir) with { Panel = Panel, Login = Login, LoginRemembered = LoginRemembered };

    // Checked before Chrome opens; the paths are kept whole, so the login is remembered whatever folder it ran in.
    private static LoginFlow CheckedLogin(string? flow, string? assembly, string? settings)
    {
        if (string.IsNullOrWhiteSpace(flow)) throw new ArgumentException("--login needs the login flow's class name.");
        if (string.IsNullOrWhiteSpace(assembly))
            throw new ArgumentException($"--login {flow} needs --login-assembly, your test project's built .dll, e.g. " +
                                        "bin/Debug/net10.0/MyTests.dll.");
        if (!File.Exists(assembly))
            throw new ArgumentException($"There is no {assembly}: build your test project first (dotnet build), or " +
                                        "record with --no-login.");
        if (settings is not null && !File.Exists(settings)) throw new ArgumentException($"There is no {settings}.");
        return new LoginFlow(flow.Trim(), Path.GetFullPath(assembly), settings is null ? null : Path.GetFullPath(settings));
    }

    // Checked before recording, so nobody records a flow that cannot be saved or compiled.
    private static RecordRequest Create(string name, string url, string outputDir)
    {
        EnsureHttpUrl(url);
        var className = FlowCodeGenerator.ToPascalCase(name);
        var clash = TypesAFlowUses.Contains(className) ? "a type its code uses"
            : WindowsDeviceName().IsMatch(className) ? "a device on Windows"
            : null;
        if (clash is not null)
            throw new ArgumentException($"A flow cannot be named {className}, {clash}. Try {className}Flow.");
        if (Encoding.UTF8.GetByteCount(className) > MaxNameBytes)
            throw new ArgumentException($"The flow's name is too long to be a file name: '{className}'.");
        return new RecordRequest(className, url, outputDir);
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
