using System.Text;
using System.Text.RegularExpressions;
using QaaS.Playwright.Flows;
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
        Func<string, string, (LoginFlow? Found, string? Why)>? find = null)
    {
        var continuing = args is ["--continue", ..];
        if (args is not ["record", ..] && !continuing)
            throw new ArgumentException($"'{string.Join(' ', args)}' is not a command of qaas-playwright. Run qaas-playwright alone " +
                                        "to be asked what to record, or qaas-playwright record <name> <url>, e.g. " +
                                        "qaas-playwright record new-order https://my-app.com/orders.");

        var outputDir = DefaultOutputDir;
        var keepRecording = false;
        var panel = true;
        var autoChecks = true;
        var noLogin = false;
        string? loginFlow = null, loginAssembly = null, loginSettings = null, continued = null;
        var positionals = new List<string>();
        for (var index = continuing ? 0 : 1; index < args.Count; index++)
        {
            string ValueOf(string option, string what, string example) =>
                index + 1 < args.Count && args[index + 1] is var value && value.Trim().Length > 0 && !value.StartsWith('-')
                    ? args[++index]
                    : throw new ArgumentException($"{option} needs {what} after it, e.g. {option} {example}.");

            switch (args[index])
            {
                case "--output-dir": outputDir = ValueOf("--output-dir", "the folder to save the flow in", "UiFlows"); break;
                case "--keep-recording": keepRecording = true; break;
                case "--no-panel": panel = false; break;
                case "--no-auto-checks": autoChecks = false; break;
                case "--login": loginFlow = ValueOf("--login", "the class name of the flow your tests log in with", "LoginFlow"); break;
                case "--login-assembly":
                    loginAssembly = ValueOf("--login-assembly", "the DLL in your test project's bin folder that has the login flow",
                        Path.Combine("bin", "Debug", "net10.0", "YourShared.dll"));
                    break;
                case "--login-settings":
                    loginSettings = ValueOf("--login-settings", "the test YAML with the values your login flow needs",
                        Path.Combine("Tests", "Smoke.qaas.yaml"));
                    break;
                case "--no-login": noLogin = true; break;
                case "--continue": continued = ValueOf("--continue", "the name of the flow to go on from, or its .cs file", "new-order"); break;
                case var option when option.StartsWith('-'):
                    throw new ArgumentException($"'{option}' is not an option of qaas-playwright: check its spelling. " +
                                                "qaas-playwright --help lists every option.");
                default: positionals.Add(args[index]); break;
            }
        }

        if (continued is not null && positionals.Count > 0)
            throw new ArgumentException("--continue takes the flow's name or its .cs file, without a name or a URL, as the " +
                                        "flow starts where it was recorded: e.g. qaas-playwright --continue new-order.");
        if (continued is null && positionals is not [_, _])
            throw new ArgumentException("record needs a name for the flow and the address to start on, in that order, e.g. " +
                                        "qaas-playwright record new-order https://my-app.com/orders. Options may follow: " +
                                        "qaas-playwright --help lists them.");
        var given = loginFlow ?? loginAssembly ?? loginSettings;
        if (noLogin && given is not null)
            throw new ArgumentException("--no-login records without logging in, so it cannot go with --login, --login-assembly " +
                                        "or --login-settings: keep either --no-login or the login options.");
        // --login alone: the flow's DLL and values as the tests have them, else why it was not found.
        var (named, whyNot) = loginFlow is not null && loginAssembly is null && find is not null ? find(loginFlow, outputDir) : (null, null);
        var login = noLogin ? null
            : given is null ? remembered is { Found: false } ? CheckedLogin(remembered.Flow, remembered.Assembly, remembered.Settings) : null
            : named is not null ? loginSettings is null ? named
                : CheckedLogin(named.Flow, named.Assembly, loginSettings) with { BaseUrlFrom = named.BaseUrlFrom }
            : CheckedLogin(loginFlow ?? remembered?.Flow, loginAssembly ?? remembered?.Assembly,
                loginSettings ?? (loginFlow is null || loginFlow == remembered?.Flow ? remembered?.Settings : null), whyNot);
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
    /// first: the one <paramref name="find"/> finds in the tests, when the folder has no login given before; else, when
    /// the person says their tests log in first, by its class name, never the flow being recorded, among the flows
    /// <paramref name="discover"/> finds, then its DLL only when none was found, and its values only when the tests' YAML
    /// does not give them and it has any.
    /// </summary>
    /// <exception cref="OperationCanceledException">The input ended before all were given.</exception>
    public static RecordRequest Ask(LoginFlow? remembered = null, Func<RecordRequest, RecordRequest?>? find = null,
        Func<string, Discovered?>? discover = null)
    {
        ConsoleUi.Intro("Let's record a browser flow. Each question says what it needs; Enter takes the answer shown in brackets.");
        var url = ConsoleUi.Ask("The address of the page to start recording on, e.g. https://my-app.com/orders. The recorder " +
                                "opens it in Chrome", "", answer => answer.Length == 0
            ? "Type the address of the page to start on, e.g. https://my-app.com/login. It must start with http:// or https://."
            : Refusal(() => EnsureHttpUrl(answer)));
        string AskName() => ConsoleUi.Ask("A name for the flow you are recording, e.g. new-order. It becomes the C# class " +
                                          "NewOrder that your tests run", "my-flow", answer => Refusal(() => Create(answer, url, DefaultOutputDir)));
        var request = Create(AskName(), url, ConsoleUi.Ask("The folder to save the flow in, e.g. Flows. Its .cs file and its " +
                                                           ".flow.yaml of values go there", DefaultOutputDir));
        // A flow of that name is there: go on from it, record it anew in its place, or name this one otherwise.
        while (File.Exists(request.CodePath))
        {
            var choice = ConsoleUi.Choose($"{request.CodePath} is there already. What should the recorder do with it?",
                ["Continue it: replay its steps, then record more after them", "Overwrite it with a new recording",
                    "Choose another name for this flow"]);
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
        return AskLogin(request, given, discover?.Invoke(request.OutputDir));
    }

    // Whether the tests log in first, and with which flow: never the one being recorded; one of those found, by its number
    // or its name, when any was; else by its name and its DLL. Its values only when nothing gives them and it has any.
    private static RecordRequest AskLogin(RecordRequest request, LoginFlow? given, Discovered? discovered)
    {
        if (!ConsoleUi.Confirm("Do your tests log in first with a flow, e.g. LoginFlow? The recorder can run that login flow " +
                               "before you record, so you start recording already signed in", yes: given is not null))
            return request with { NoLogin = true };

        var flows = discovered?.Flows ?? [];
        var listed = discovered is null ? [] : discovered.Likely.Concat(flows.Except(discovered.Likely)).ToList();
        if (listed.Count > 0)
        {
            ConsoleUi.Info($"The flows of your tests, in {ConsoleUi.Relative(discovered!.Build!.Dll)} and the DLLs it uses:");
            ConsoleUi.Options([.. listed.Select(ConsoleUi.Described)]);
        }

        var flow = ConsoleUi.Ask("Its class name (the login flow, not the one you're recording), e.g. LoginFlow" +
                                 (listed.Count > 0 ? ", or its number above" : ""),
            given?.Flow ?? discovered?.Likely.FirstOrDefault()?.Name ?? "", answer =>
            {
                if (answer.Length == 0) return "Type the class name of the flow your tests log in with, e.g. LoginFlow; or none to record without logging in.";
                if (answer.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
                if (int.TryParse(answer, out var number))
                    return number >= 1 && number <= listed.Count ? null : $"Type a number from 1 to {listed.Count}, or the flow's class name.";
                if (SameFlow(answer, request.ClassName))
                    return $"{answer} is the flow you are recording now, so it cannot log you in first. Type the flow your " +
                           "tests run before each test to log in, e.g. LoginFlow; or none to record without logging in.";
                if (flows.Count == 0 || SameFlow(answer, given?.Flow ?? "")
                    || FlowDiscovery.Named(answer, flows.Select(found => found.Type.Assembly).Distinct()).Count == 1) return null;
                return $"There is no flow named {answer} in your tests' build ({ConsoleUi.Relative(discovered!.Build!.Dll)}) or " +
                       "the DLLs it uses, so QaaS could not run it either. Type one of the flows listed above, by its number " +
                       "or its name; build your tests first if the flow is new (dotnet build); or type none.";
            });
        if (flow.Equals("none", StringComparison.OrdinalIgnoreCase)) return request with { NoLogin = true };
        var picked = int.TryParse(flow, out var number) ? listed[number - 1]
            : flows.FirstOrDefault(found => found.Name.Equals(flow, StringComparison.OrdinalIgnoreCase));

        LoginFlow login;
        Type? type = picked?.Type;
        if (picked is not null)
        {
            login = LoginFinder.LoginOf(picked, discovered!);
        }
        else
        {
            var example = Path.Combine("bin", "Debug", "net10.0", "YourShared.dll");
            var assembly = ConsoleUi.Ask($"The DLL that has {flow}, e.g. {example}. It is in your test project's bin folder " +
                                         "once built (dotnet build); the recorder did not find it by itself",
                given?.Assembly ?? "", answer => Refusal(() => type = FlowIn(flow, CheckedLogin(flow, answer, null).Assembly)));
            login = CheckedLogin(flow, assembly, null);
            flow = login.Flow;
        }

        // Its values: those its YAML gives, found; asked only when it has any, and nothing gives them.
        var needsValues = type is null || LoginValues.Of(type, null).Has;
        if (login.Settings is not null || !needsValues) return request with { Login = login };
        var settings = ConsoleUi.Ask($"Values {login.Flow} needs (a YAML with FlowConfiguration: {login.Flow}:), e.g. " +
                                     $"{Path.Combine("Tests", "Smoke.qaas.yaml")}, which gives its user name. Enter if it needs none",
            given?.Settings ?? "none", answer => answer.Equals("none", StringComparison.OrdinalIgnoreCase) || File.Exists(answer)
                ? null
                : $"There is no {answer}. Type the path of the test YAML that has the block FlowConfiguration: {login.Flow}:, " +
                  "e.g. Tests/Smoke.qaas.yaml; or press Enter if it needs no values.");
        return request with
        {
            Login = login with { Settings = settings.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : Path.GetFullPath(settings) },
        };
    }

    // The flow named so in the DLL and what it uses, found as QaaS finds it.
    private static Type FlowIn(string flow, string assembly)
    {
        IReadOnlyList<System.Reflection.Assembly> loaded;
        try
        {
            loaded = FlowAssemblyContext.LoadWithFlows(assembly).Loaded;
        }
        catch (Exception failure) when (failure is BadImageFormatException or FileLoadException or FileNotFoundException)
        {
            throw new ArgumentException($"{assembly} could not be loaded ({failure.Message}). Give the DLL from your test " +
                                        "project's bin folder, built with dotnet build.");
        }

        var names = string.Join(", ", FlowDiscovery.FlowsIn(loaded).Select(type => type.Name).Distinct().Take(8));
        return FlowDiscovery.Named(flow, loaded) switch
        {
            [var one] => one,
            [] => throw new ArgumentException($"There is no flow named {flow} in {Path.GetFileName(assembly)} or the DLLs it uses " +
                                              $"(its flows: {(names.Length > 0 ? names : "none")}). Give the DLL that has it, or " +
                                              "check its class name."),
            var several => throw new ArgumentException($"{flow} is in {several.Count} places ({string.Join(", ", several.Select(type => type.FullName))}), " +
                                                       "so QaaS could not tell which to run. Rename one of them."),
        };
    }

    private static bool SameFlow(string answer, string className)
    {
        try
        {
            return string.Equals(FlowCodeGenerator.ToPascalCase(answer), className, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(answer, className, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
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
    private static LoginFlow CheckedLogin(string? flow, string? assembly, string? settings, string? whyNotFound = null)
    {
        if (string.IsNullOrWhiteSpace(flow))
            throw new ArgumentException("--login-assembly and --login-settings need --login with the login flow's class name, " +
                                        "e.g. --login LoginFlow.");
        if (string.IsNullOrWhiteSpace(assembly))
            throw new ArgumentException(whyNotFound ??
                                        $"{flow} is not in a build of the test project here. Build it (dotnet build), or " +
                                        $"give --login-assembly: the DLL in your test project's bin folder that has {flow}, " +
                                        $"e.g. {Path.Combine("bin", "Debug", "net10.0", "YourShared.dll")}.");
        if (!File.Exists(assembly))
            throw new ArgumentException($"There is no {assembly}, so {flow} cannot run. Build your test project first " +
                                        "(dotnet build), give the right path, or record with --no-login.");
        return new LoginFlow(flow.Trim(), Path.GetFullPath(assembly), settings is null ? null : Path.GetFullPath(CheckedFile(settings)));
    }

    private static string CheckedFile(string settings) => File.Exists(settings)
        ? settings
        : throw new ArgumentException($"There is no {settings}, the YAML with your login flow's values. Give the path of the " +
                                      "test YAML that has them (under FlowConfiguration: <flow>:), or leave out --login-settings.");

    private static RecordRequest Create(string name, string url, string outputDir)
    {
        EnsureHttpUrl(url);
        return new RecordRequest(ClassNameOf(name), url, outputDir);
    }

    // Checked before recording, so nobody records a flow that cannot be saved or compiled.
    private static string ClassNameOf(string name)
    {
        var className = FlowCodeGenerator.ToPascalCase(name);
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

    [GeneratedRegex("^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])$", RegexOptions.IgnoreCase)]
    private static partial Regex WindowsDeviceName();

    private static void EnsureHttpUrl(string url)
    {
        var isHttp = Uri.TryCreate(url, UriKind.Absolute, out var uri)
                     && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        if (!isHttp)
            throw new ArgumentException($"'{url}' is not an address the recorder can open: it must start with http:// or " +
                                        "https://, e.g. https://my-app.com/login.");
    }
}
