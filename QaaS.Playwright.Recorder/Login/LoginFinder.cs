using QaaS.Playwright.Recorder.TestRun;

namespace QaaS.Playwright.Recorder.Login;

/// <summary>A flow of the tests that may log in, the DLL that has it, and how many test files run it first.</summary>
internal sealed record FoundFlow(Type Type, string Dll, int SetupIn)
{
    public string Name => Type.Name;
}

/// <summary>
/// Finds the login flow of the test project the recorder runs in, so nobody has to name it: the flow the project's
/// YAML runs as a setup flow (<c>SetupFlows: [LoginFlow]</c>) in the most files, else the one flow whose name says it
/// logs in, found in the project's build and the DLLs beside it, as a shared package's flows are. Its values come from
/// the <c>FlowConfiguration: LoginFlow:</c> of a file that runs it. The first time in a folder the person is asked,
/// and the answer is remembered; later the same flow is found again, in the newest build.
/// </summary>
internal static class LoginFinder
{
    // Words in a flow's name that say it logs in, when no test runs a setup flow.
    private static readonly string[] LoginWords = ["Login", "LogIn", "SignIn", "Auth"];

    /// <summary>
    /// The request with the login flow found for it, asked about or as remembered for <paramref name="folder"/>; the
    /// request as it is when the person said no; null when nothing was found, or the person said no before.
    /// </summary>
    /// <exception cref="OperationCanceledException">The input ended at a question.</exception>
    public static RecordRequest? Offer(RecordRequest request, string folder)
    {
        if (RememberedLogin.Declined(folder)) return null;
        var remembered = RememberedLogin.In(folder) is { Found: true } found ? found : null;
        if ((TestProject.Near(request.OutputDir) ?? TestProject.Near(folder)) is not { } project) return null;

        var (files, codeChanged, projectChanged) = project.Look();
        var setupIn = files.SelectMany(file => file.SetupFlows).GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(names => names.Key, names => names.Count(), StringComparer.OrdinalIgnoreCase);
        var expected = remembered?.Flow ?? setupIn.OrderByDescending(pair => pair.Value).Select(pair => pair.Key).FirstOrDefault();
        if (Built(project, expected, remembered, codeChanged, projectChanged, setupIn) is not { } build) return null;

        var flows = FlowsOf(build, setupIn, warn: expected is not null);
        if (remembered is not null && flows.FirstOrDefault(flow => SameName(flow.Name, remembered.Flow)) is { } again)
            return Take(request, folder, again, files, Told.OnlyWhatIsWrong);
        return Likely(flows) switch
        {
            [] => null,
            [var one] => Take(request, folder, one, files, Told.AndAsked),
            var several => ChooseAmong(request, folder, several, files),
        };
    }

    /// <summary>
    /// The flow named <paramref name="flow"/> in the newest build of the test project of <paramref name="outputDir"/>,
    /// with its values from the project's YAML, for <c>--login</c> given alone; null when it is not there.
    /// </summary>
    public static LoginFlow? Named(string flow, string outputDir)
    {
        if ((TestProject.Near(outputDir) ?? TestProject.Near(Environment.CurrentDirectory)) is not { } project
            || project.NewestBuild() is not { } build)
            return null;
        var found = FlowsOf(build, [], warn: false).FirstOrDefault(candidate => SameName(candidate.Name, flow));
        return found is null ? null : new LoginFlow(found.Name, found.Dll, SettingsOf(found.Name, project.Look().Files), Found: true);
    }

    // What the person is told of the flow found: all of it, and asked; all of it, chosen; or only what is wrong.
    private enum Told { AndAsked, Chosen, OnlyWhatIsWrong }

    // The flows the person most likely logs in with: the setup flow most files run, a login among those run as often,
    // else the flows named as a login is.
    private static List<FoundFlow> Likely(IReadOnlyList<FoundFlow> flows)
    {
        var setup = flows.Where(flow => flow.SetupIn > 0).ToList();
        if (setup.Count == 0) return [.. flows.Where(LooksLikeLogin)];
        var most = setup.Max(flow => flow.SetupIn);
        var likely = setup.Where(flow => flow.SetupIn == most).ToList();
        return likely.Count(LooksLikeLogin) == 1 ? [likely.Single(LooksLikeLogin)] : likely;
    }

    private static bool LooksLikeLogin(FoundFlow flow) => LoginWords.Any(word => flow.Name.Contains(word, StringComparison.Ordinal));

    private static RecordRequest Take(RecordRequest request, string folder, FoundFlow flow, IReadOnlyList<TestFile> files, Told told)
    {
        var settings = SettingsOf(flow.Name, files);
        var values = LoginValues.Of(flow.Type, settings);
        switch (told)
        {
            case Told.AndAsked:
                ConsoleUi.FoundLogin(flow, settings, values);
                if (ConsoleUi.Confirm($"Log in first with {flow.Name}")) break;
                return Declined(request, folder, flow.Name);
            case Told.Chosen:
                ConsoleUi.LoginValuesOf(flow.Name, settings, values);
                break;
            default:
                ConsoleUi.LoginValuesOf(flow.Name, settings, values, onlyWrong: true);
                break;
        }

        var login = new LoginFlow(flow.Name, flow.Dll, settings, Found: true);
        Remembered(RememberedLogin.Remember(folder, login));
        return request with { Login = login, LoginRemembered = false };
    }

    private static RecordRequest ChooseAmong(RecordRequest request, string folder, IReadOnlyList<FoundFlow> flows, IReadOnlyList<TestFile> files)
    {
        var choice = ConsoleUi.Choose($"Your tests have {flows.Count} flows that may log in. Log in first with",
            [.. flows.Select(ConsoleUi.Described), "None: record without logging in"]);
        return choice < flows.Count ? Take(request, folder, flows[choice], files, Told.Chosen) : Declined(request, folder, null);
    }

    private static RecordRequest Declined(RecordRequest request, string folder, string? flow)
    {
        Remembered(RememberedLogin.Decline(folder));
        ConsoleUi.Info($"Recording without logging in, here from now on. To log in first later, record with --login {flow ?? "<flow>"}.");
        return request;
    }

    private static void Remembered(string? notRemembered)
    {
        if (notRemembered is not null) ConsoleUi.Warning(notRemembered);
    }

    // The file whose FlowConfiguration has the flow's values: one that runs it as a setup flow first, the nearest the
    // project's folder first.
    private static string? SettingsOf(string flow, IReadOnlyList<TestFile> files) => files
        .Where(file => LoginSettings.Find(file.Yaml, flow)?.GetChildren().Any() == true)
        .OrderBy(file => file.SetupFlows.Contains(flow) ? 0 : 1)
        .ThenBy(file => file.Path.Count(character => character == Path.DirectorySeparatorChar))
        .ThenBy(file => file.Path, StringComparer.Ordinal)
        .Select(file => file.Path)
        .FirstOrDefault();

    // The flows of the build and of the DLLs beside it; a name two of them have is left out, as the probe could not
    // tell them apart.
    private static List<FoundFlow> FlowsOf(ProjectBuild build, Dictionary<string, int> setupIn, bool warn)
    {
        try
        {
            return [.. FlowAssemblyContext.FlowsIn(FlowAssemblyContext.LoadWithFlows(build.Dll))
                .GroupBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
                .Where(named => named.Count() == 1)
                .Select(named => named.Single())
                .Select(type => new FoundFlow(type, string.IsNullOrEmpty(type.Assembly.Location) ? build.Dll : type.Assembly.Location,
                    setupIn.GetValueOrDefault(type.Name)))];
        }
        catch (Exception failure) when (failure is BadImageFormatException or FileLoadException or FileNotFoundException)
        {
            if (warn) ConsoleUi.Warning($"Could not look for your login flow in {ConsoleUi.Relative(build.Dll)}: {failure.Message}");
            return [];
        }
    }

    // The newest build, built first when the person agrees, when there is none or the code changed since. Only asked
    // when the tests run a setup flow, or a login was found here before: a project without one is none of the
    // recorder's business. Null when there is no build to look in.
    private static ProjectBuild? Built(TestProject project, string? expected, LoginFlow? remembered, DateTime codeChanged,
        DateTime projectChanged, Dictionary<string, int> setupIn)
    {
        var build = project.NewestBuild();
        if (expected is null) return build;
        // A shared package's flow does not change with the project's own code.
        var inProject = remembered is null || build is null || Same(remembered.Assembly, build.Dll);
        var stale = build is not null && (build.BuiltAt < projectChanged || inProject && build.BuiltAt < codeChanged);
        if (build is not null && !stale) return build;

        var command = BuildCommand(project, build);
        var why = setupIn.TryGetValue(expected, out var count)
            ? $"{expected}, the setup flow of {ConsoleUi.Count(count, "test file")}"
            : expected;
        ConsoleUi.Info(build is null
            ? $"{project.Name} is not built yet, so {why}, cannot run."
            : $"{project.Name} changed since it was built, {ConsoleUi.Ago(build.BuiltAt.ToLocalTime())}.");
        if (!ConsoleUi.Confirm(build is null ? $"Build it now with {command}" : $"Build it again with {command}"))
        {
            ConsoleUi.Info(build is null
                ? $"Not logging in this time. To log in first with {expected}, build it: {command}"
                : $"Logging in with its last build. To build it again: {command}");
            return build;
        }

        ConsoleUi.Phase($"Building {project.Name}");
        var (exitCode, log) = FlowBuild.DotnetAsync(project.Folder,
            ["build", project.ProjectFile, "--nologo", "-v", "q", "-c", build?.Configuration ?? "Debug"], CancellationToken.None)
            .GetAwaiter().GetResult();
        if (exitCode == 0) return project.NewestBuild() ?? build;
        ConsoleUi.BuildFailed(project.Name, log, build is null ? null : command);
        return build;
    }

    // "dotnet build" in the project's folder; else with the project's path, and the configuration of its last build.
    private static string BuildCommand(TestProject project, ProjectBuild? build)
    {
        var here = Same(project.Folder, Environment.CurrentDirectory);
        var command = here ? "dotnet build" : $"dotnet build {ConsoleUi.Relative(project.ProjectFile)}";
        return build is { Configuration: var configuration } && !SameName(configuration, "Debug") ? $"{command} -c {configuration}" : command;
    }

    private static bool SameName(string one, string other) => string.Equals(one, other, StringComparison.OrdinalIgnoreCase);

    private static bool Same(string one, string other) =>
        string.Equals(Path.TrimEndingDirectorySeparator(one), Path.TrimEndingDirectorySeparator(other),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
