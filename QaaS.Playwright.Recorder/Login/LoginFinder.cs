using System.Reflection;
using QaaS.Playwright.Flows;
using QaaS.Playwright.Recorder.TestRun;

namespace QaaS.Playwright.Recorder.Login;

/// <summary>A flow of the tests that may log in, the DLL that has it, and how many test files run it first.</summary>
internal sealed record FoundFlow(Type Type, string Dll, int SetupIn)
{
    public string Name => Type.Name;
}

/// <summary>
/// What the search found in a test project: its YAML files, its build, the flows of the build and of what it uses, the
/// ones that most likely log in, and what is wrong, said for a person.
/// </summary>
internal sealed class Discovered
{
    public TestProject? Project { get; init; }

    public IReadOnlyList<TestFile> Files { get; set; } = [];

    public Dictionary<string, int> SetupIn { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public ProjectBuild? Build { get; set; }

    public List<FoundFlow> Flows { get; set; } = [];

    /// <summary>The flows that most likely log in, each of which can run.</summary>
    public List<FoundFlow> Likely { get; set; } = [];

    /// <summary>The flow remembered for the folder, found again; null when none was, or it is not there now.</summary>
    public FoundFlow? Again { get; set; }

    /// <summary>What stops the tests' login flow from running in the recorder, each said with what to do.</summary>
    public List<string> Problems { get; } = [];

    /// <summary>When the project's code, and the project itself, last changed: a build older is out of date.</summary>
    public DateTime CodeChanged { get; set; }

    public DateTime ProjectChanged { get; set; }
}

/// <summary>
/// What the login search found, as the start screen shows it, without asking anything or building: the test projects to
/// choose among when several use QaaS and none was chosen, what the search found in the one taken, why its build may be
/// out of date and the command that builds it, and each step of the search.
/// </summary>
internal sealed record LoginLook(LoginSearch Search, IReadOnlyList<TestProject> Projects, Discovered? Discovered, string? Stale,
    string? BuildCommand)
{
    /// <summary>The flow to log in with by default: the one remembered here, found again, else the likeliest.</summary>
    public FoundFlow? Suggested => Discovered is { Problems.Count: 0 } found ? found.Again ?? found.Likely.FirstOrDefault() : null;
}

/// <summary>
/// Finds the login flow of the test project the recorder runs in, as QaaS finds a setup flow when the tests run, so
/// nobody has to name it. The project is the nearest .csproj from the output folder or the current one up, else one up to
/// <see cref="TestProject.Depth"/> folders below, or in a .sln or .slnx there, that uses QaaS (asked once when several
/// do). Its build is the newest in its bin folder, or where MSBuild says it builds to. Its flows are those of the build
/// and of its dependency closure (its .deps.json and its folder), matched by class name, case-insensitively, as
/// <see cref="FlowDiscovery"/> matches them: the flow the project's YAML runs as a setup flow
/// (<c>SetupFlows: [LoginFlow]</c>) in the most files, else the one flow whose name says it logs in. A name two flows
/// have, or a flow built against a QaaS.Playwright the recorder cannot run, is said plainly. Its values come from the
/// <c>FlowConfiguration: LoginFlow:</c> of a file that runs it, and its BaseUrl from that file's probe. The first time in
/// a folder the person is asked, and the answer is remembered; later the same flow is found again, in the newest build.
/// When nothing is found, one line says why, and the start screen's Why no login? shows the whole search.
/// </summary>
internal static class LoginFinder
{
    // Words in a flow's name that say it logs in, when no test runs a setup flow.
    private static readonly string[] LoginWords = ["Login", "LogIn", "SignIn", "Auth"];

    /// <summary>The login flow named <paramref name="flow"/> as found in <paramref name="discovered"/>, with its values and BaseUrl.</summary>
    public static LoginFlow LoginOf(FoundFlow flow, Discovered discovered) =>
        new(flow.Name, LoadPath(flow, discovered.Build), SettingsOf(flow.Name, discovered.Files), Found: true,
            discovered.Project?.ProjectFile, BaseUrlFileOf(flow.Name, discovered.Files));

    /// <summary>The file of the tests' YAML that gives <paramref name="flow"/>'s values; null when none does.</summary>
    public static string? SettingsIn(Discovered discovered, string flow) => SettingsOf(flow, discovered.Files);

    /// <summary>
    /// Prints the whole search: the projects found, the build used, the DLLs scanned, the flows
    /// found and why each was chosen or not, and what recording here would do. Asks nothing and builds nothing.
    /// </summary>
    public static void Explain(string outputDir, string folder) => ConsoleUi.LoginSearch(Explained(outputDir, folder));

    /// <summary>
    /// The whole search, as the start screen's Why sheet shows it: the projects found and why each was taken or not, the
    /// build used, the DLLs looked through, the flows found and why each was chosen or not, and what recording here would do.
    /// </summary>
    public static LoginSearch Explained(string outputDir, string folder)
    {
        var search = new LoginSearch();
        search.Say($"Looking for the login flow of your tests, from {Path.GetFullPath(folder)}");
        search.Say(RememberedLogin.DeclinedIn(folder) is { } no ? $"Remembered here: {NotLoggingInHere(no)}"
            : RememberedLogin.In(folder) is { } login ? $"Remembered here: log in with {login.Flow}, from {ConsoleUi.Relative(login.Assembly)}" +
                                                        (login.Found ? " (found again in the newest build each time)" : " (chosen on the start screen)")
            : RememberedLogin.ProjectIn(folder) is { } project ? $"Remembered here: the test project {ConsoleUi.Relative(project)}"
            : "Remembered here: nothing yet.", 1);
        var any = false;
        foreach (var discovered in Each(outputDir, folder, search))
        {
            any = true;
            search.Say(discovered.Likely switch
            {
                _ when discovered.Problems.Count > 0 => $"Result: no login, because: {string.Join(" ", discovered.Problems)}",
                [] => $"Result: no login flow in {discovered.Project?.Name}: {search.Why ?? "none of its flows logs in"}.",
                [var one] => $"Result: recording here logs in first with {one.Name}, from {ConsoleUi.Relative(one.Dll)}.",
                var several => $"Result: choose which of {string.Join(", ", several.Select(flow => flow.Name))} logs in, on the Log in first card.",
            }, 1);
        }

        if (!any) search.Say($"Result: {NoLoginFlow(search)}", 1);
        return search;
    }

    /// <summary>
    /// What the search finds for the start screen, asking nothing and building nothing: the project, its newest build,
    /// its flows, the likely ones, and whether the build is older than the project.
    /// </summary>
    public static LoginLook Look(string outputDir, string folder)
    {
        var search = new LoginSearch();
        search.Say($"Looking for the login flow of your tests, from {Path.GetFullPath(folder)}");
        var remembered = RememberedLogin.In(folder) is { Found: true } found ? found : null;
        var (project, several) = FindProject(outputDir, folder, search);
        if (project is null) return new LoginLook(search, several, null, null, null);
        var discovered = SearchIn(project, search, remembered);
        var expected = remembered?.Flow ?? discovered.SetupIn.OrderByDescending(pair => pair.Value).Select(pair => pair.Key).FirstOrDefault();
        var build = discovered.Build;
        var inProject = remembered is null || build is null || Same(remembered.Assembly, build.Dll);
        var stale = expected is null && build is not null ? null
            : build is null ? $"{project.Name} is not built yet, so its login flow cannot run."
            : build.BuiltAt < discovered.ProjectChanged || inProject && build.BuiltAt < discovered.CodeChanged
                ? $"{project.Name} changed since it was built, {ConsoleUi.Ago(build.BuiltAt.ToLocalTime())}: its login flow may be out of date."
                : null;
        return new LoginLook(search, [], discovered, stale, BuildCommand(project, build));
    }

    /// <summary>Builds the test project as its last build was built, for the start screen's Build now.</summary>
    public static Task<(int ExitCode, string Log)> BuildAsync(TestProject project, ProjectBuild? build, CancellationToken stop) =>
        FlowBuild.DotnetAsync(project.Folder, ["build", project.ProjectFile, "--nologo", "-v", "q", "-c", build?.Configuration ?? "Debug"], stop);

    /// <summary>The files of the tests' YAML that give <paramref name="flow"/>'s values, the likeliest first.</summary>
    public static IReadOnlyList<string> SettingsFilesOf(Discovered discovered, string flow) => [.. discovered.Files
        .Where(file => LoginSettings.Find(file.Yaml, flow)?.GetChildren().Any() == true)
        .OrderBy(file => file.SetupFlows.Contains(flow) ? 0 : 1)
        .ThenBy(file => file.Path.Count(character => character == Path.DirectorySeparatorChar))
        .ThenBy(file => file.Path, StringComparer.Ordinal)
        .Select(file => file.Path)];

    // "Not logging in here (turned off on 2026-11-03)."
    private static string NotLoggingInHere(DeclinedLogin no) =>
        $"Not logging in here (Log in first turned off{(no.On is { } on ? $" on {on:yyyy-MM-dd}" : "")}).";

    private static string NoLoginFlow(LoginSearch search) =>
        $"No login flow: {search.Why ?? "none of your tests' flows logs in"}. To log in first, choose your test project's folder " +
        "on the start screen.";

    // The search in each test project found: the one taken, or each of those that use QaaS, when several do and the
    // person has not chosen one; nothing asked.
    private static IEnumerable<Discovered> Each(string outputDir, string folder, LoginSearch search)
    {
        var (project, several) = FindProject(outputDir, folder, search);
        foreach (var each in project is null ? several : [project])
        {
            if (project is null) search.Say($"In {each.Name} ({ConsoleUi.Relative(each.ProjectFile)}):", 1);
            yield return SearchIn(each, search, remembered: null);
        }
    }

    // The test project: the nearest .csproj from the output folder or the current one up; else those below the current
    // one and in its solutions that use QaaS: the one, the one chosen here before, or the one the person chooses now.
    private static (TestProject? Project, List<TestProject> Several) FindProject(string outputDir, string folder, LoginSearch search)
    {
        foreach (var from in new[] { outputDir, folder }.Select(Path.GetFullPath).Distinct())
        {
            if (TestProject.Near(from) is not { } near) continue;
            search.Say($"Test project: {ConsoleUi.Relative(near.ProjectFile)}, the nearest .csproj in {from} or a folder above it.", 1);
            return (near, []);
        }

        var here = Path.GetFullPath(folder);
        search.Say($"No .csproj in {here} or a folder above it: looking up to {TestProject.Depth} folders below it, and in its .sln and .slnx files.", 1);
        var below = TestProject.Below(folder);
        if (below.Count == 0)
        {
            search.None($"no test project in {here}, above it or up to {TestProject.Depth} folders below it");
            return (null, []);
        }

        // A project whose YAML runs setup flows is a test project for sure; one that only took QaaS packages may be the
        // team's shared one, taken only when no project runs setup flows.
        var (withSetupFlows, withPackages) = (new List<TestProject>(), new List<TestProject>());
        foreach (var (project, where) in below)
        {
            var uses = project.UsesQaaS(out var runsSetupFlows);
            search.Say($"{ConsoleUi.Relative(project.ProjectFile)} ({where}): " + (uses is null
                ? "not taken, as it does not use QaaS (no QaaS package in its restore, and no YAML of it runs SetupFlows)"
                : $"uses QaaS: {uses}"), 2);
            if (uses is not null) (runsSetupFlows ? withSetupFlows : withPackages).Add(project);
        }

        var withQaaS = withSetupFlows.Count > 0 ? withSetupFlows : withPackages;
        if (withSetupFlows.Count > 0 && withPackages.Count > 0)
            search.Say($"Only those whose YAML runs SetupFlows are taken: {string.Join(", ", withSetupFlows.Select(project => project.Name))}.", 1);

        switch (withQaaS)
        {
            case []:
                search.None($"none of the {ConsoleUi.Count(below.Count, "project")} in {here} or below it uses QaaS");
                return (null, []);
            case [var one]:
                search.Say($"Taken: {one.Name}, the one that uses QaaS.", 1);
                return (one, []);
        }

        if (RememberedLogin.ProjectIn(folder) is { } kept && withQaaS.FirstOrDefault(project => Same(project.ProjectFile, kept)) is { } chosen)
        {
            search.Say($"Taken: {chosen.Name}, which you chose here before.", 1);
            return (chosen, []);
        }

        search.Say($"{withQaaS.Count} of them use QaaS: the start screen asks which one, once, and remembers it. Each:", 1);
        return (null, withQaaS);
    }

    // The project's YAML, its newest build, its flows, and the likely ones.
    private static Discovered SearchIn(TestProject project, LoginSearch search, LoginFlow? remembered)
    {
        var discovered = new Discovered { Project = project };
        var (files, codeChanged, projectChanged) = project.Look();
        discovered.Files = files;
        (discovered.CodeChanged, discovered.ProjectChanged) = (codeChanged, projectChanged);
        discovered.SetupIn = files.SelectMany(file => file.SetupFlows).GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(names => names.Key, names => names.Count(), StringComparer.OrdinalIgnoreCase);
        search.Say(discovered.SetupIn.Count == 0
            ? $"The YAML of {project.Name}: no file runs a setup flow, so a flow whose name says it logs in is looked for."
            : $"The YAML of {project.Name} runs setup flows: " +
              string.Join(", ", discovered.SetupIn.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key} (in {ConsoleUi.Count(pair.Value, "file")})")), 1);
        var expected = remembered?.Flow ?? discovered.SetupIn.OrderByDescending(pair => pair.Value).Select(pair => pair.Key).FirstOrDefault();
        var changed = codeChanged > projectChanged ? codeChanged : projectChanged;
        discovered.Build = project.NewestBuild(changed, search);
        if (discovered.Build is not { } build)
        {
            search.None($"{project.Name} is not built yet, so its flows cannot be looked at. Build it: {BuildCommand(project, null)}");
            return discovered;
        }

        FlowsOf(project, build, discovered, search);
        Choose(discovered, expected, remembered, search);
        return discovered;
    }

    // The flows of the build and of what it uses; a name two of them have is left out, as the probe could not tell them
    // apart, and said.
    private static void FlowsOf(TestProject project, ProjectBuild build, Discovered discovered, LoginSearch search)
    {
        foreach (var assets in project.AssetsFiles().Concat(ProjectBuilds.AssetsOf(project) is { } more ? [more] : []))
            FlowAssemblyContext.AddPackageFoldersOf(assets);
        FlowAssemblies loaded;
        try
        {
            loaded = FlowAssemblyContext.LoadWithFlows(build.Dll);
        }
        catch (Exception failure) when (failure is BadImageFormatException or FileLoadException or FileNotFoundException)
        {
            var why = $"the recorder could not load {ConsoleUi.Relative(build.Dll)}: {failure.Message}. Build {project.Name} again " +
                      $"({BuildCommand(project, build)}), then record again";
            search.None(why);
            discovered.Problems.Add($"Your login flow cannot be looked for: {why}.");
            return;
        }

        var canHave = loaded.Scanned.Where(dll => dll.BuiltAgainst is not null).ToList();
        search.Say($"DLLs: {loaded.Scanned.Count} looked at (the build, what its .deps.json lists, and its folder); " +
                   $"{canHave.Count} reference QaaS.Playwright, so can have flows:", 1);
        foreach (var dll in canHave)
            search.Say($"{Path.GetFileName(dll.Path)} ({dll.Why}, built against QaaS.Playwright {dll.BuiltAgainst!.ToString(3)})" +
                       (dll.NotLoaded is null ? "" : $": not loaded, {dll.NotLoaded}"), 2);

        var flows = new List<FoundFlow>();
        foreach (var named in FlowDiscovery.FlowsIn(loaded.Loaded).GroupBy(type => type.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (named.Count() > 1)
            {
                var where = string.Join(", ", named.Select(type => $"{type.FullName} ({FileOf(type.Assembly, build)})"));
                search.Say($"{named.Key}: in {named.Count()} places ({where}), so QaaS refuses it, and so does the recorder.", 2);
                if (discovered.SetupIn.ContainsKey(named.Key))
                    discovered.Problems.Add($"{named.Key}, the login flow of your tests, is in {named.Count()} places: {where}. QaaS " +
                                            "cannot tell which to run, nor can the recorder. Rename one of them, build again, and record again; " +
                                            "or turn Log in first off.");
                continue;
            }

            var type = named.Single();
            flows.Add(new FoundFlow(type, string.IsNullOrEmpty(type.Assembly.Location) ? build.Dll : FlowAssemblyContext.Original(type.Assembly.Location),
                discovered.SetupIn.GetValueOrDefault(type.Name)));
        }

        discovered.Flows = flows;
        // A setup flow of the tests that is not among them may be there, but not load.
        foreach (var setup in discovered.SetupIn.Keys.Where(name => !flows.Any(flow => SameName(flow.Name, name))
                                                                   && !discovered.Problems.Any(problem => problem.StartsWith($"{name},", StringComparison.Ordinal))))
        {
            if (loaded.Loaded.Select(assembly => FlowCheck.CannotLoad(setup, assembly)).FirstOrDefault(why => why is not null) is { } cannot)
            {
                search.Say($"{setup}: can't load. {cannot}", 2);
                discovered.Problems.Add(cannot);
            }
            else
            {
                search.Say($"{setup}: your tests run it first, but no flow of that name is in the build or the DLLs it uses.", 2);
            }
        }
    }

    // The flows the person most likely logs in with, each said with why it is one or not: the remembered one, found
    // again; the setup flow most files run, a login among those run as often; else the flows named as a login is.
    private static void Choose(Discovered discovered, string? expected, LoginFlow? remembered, LoginSearch search)
    {
        var flows = discovered.Flows;
        search.Say(flows.Count == 0 ? "Flows: none." : $"Flows: {flows.Count}.", 1);
        if (remembered is not null && flows.FirstOrDefault(flow => SameName(flow.Name, remembered.Flow)) is { } again)
        {
            if (FlowCheck.CannotRun(again.Type) is { } cannot) discovered.Problems.Add(cannot);
            else discovered.Again = again;
        }

        var likely = Likely(flows);
        foreach (var flow in flows)
        {
            var taken = discovered.Again == flow || discovered.Again is null && likely.Contains(flow);
            var cannot = taken ? FlowCheck.CannotRun(flow.Type) : null;
            if (cannot is not null)
            {
                likely.Remove(flow);
                if (!discovered.Problems.Contains(cannot)) discovered.Problems.Add(cannot);
            }

            search.Say($"{flow.Name} ({Path.GetFileName(flow.Dll)}): " + (cannot is not null ? $"can't run. {cannot}"
                : discovered.Again == flow ? "taken: you logged in with it here before"
                : taken && flow.SetupIn > 0 ? $"taken: the setup flow of {ConsoleUi.Count(flow.SetupIn, "test file")}"
                : taken ? "taken: no test runs a setup flow, and its name says it logs in"
                : flow.SetupIn > 0 ? $"not taken: the setup flow of only {ConsoleUi.Count(flow.SetupIn, "test file")}, fewer than another"
                : discovered.SetupIn.Count > 0 ? "not taken: no test runs it first"
                : "not taken: its name does not say it logs in"), 2);
        }

        discovered.Likely = discovered.Again is null ? likely : [];
        if (discovered.Likely.Count == 0 && discovered.Again is null && discovered.Problems.Count == 0 && search.Why is null)
            search.None(expected is not null && discovered.SetupIn.ContainsKey(expected)
                ? $"your tests run {expected} first, but it is not in {Built(discovered)} or the DLLs it uses; check that " +
                  $"{discovered.Project?.Name} references the package that has it, and build it"
                : flows.Count == 0
                    ? $"{discovered.Project?.Name}'s build and the DLLs it uses have no flows"
                    : $"no test of {discovered.Project?.Name} runs a setup flow, and no flow's name says it logs in " +
                      $"({string.Join(", ", flows.Select(flow => flow.Name).Take(5))})");
    }

    private static List<FoundFlow> Likely(IReadOnlyList<FoundFlow> flows)
    {
        var setup = flows.Where(flow => flow.SetupIn > 0).ToList();
        if (setup.Count == 0) return [.. flows.Where(LooksLikeLogin)];
        var most = setup.Max(flow => flow.SetupIn);
        var likely = setup.Where(flow => flow.SetupIn == most).ToList();
        return likely.Count(LooksLikeLogin) == 1 ? [likely.Single(LooksLikeLogin)] : likely;
    }

    private static bool LooksLikeLogin(FoundFlow flow) => LoginWords.Any(word => flow.Name.Contains(word, StringComparison.Ordinal));

    private static string Built(Discovered discovered) =>
        discovered.Build is { } build ? ConsoleUi.Relative(build.Dll) : "the build";

    // A flow in the build's folder runs from its DLL there; one elsewhere, e.g. a package
    // a library's build does not copy, from the build, which loads what it uses.
    private static string LoadPath(FoundFlow flow, ProjectBuild? build) =>
        build is null || Same(Path.GetDirectoryName(flow.Dll)!, Path.GetDirectoryName(build.Dll)!) ? flow.Dll : build.Dll;

    private static string FileOf(Assembly assembly, ProjectBuild build) =>
        Path.GetFileName(string.IsNullOrEmpty(assembly.Location) ? build.Dll : FlowAssemblyContext.Original(assembly.Location));

    // The file whose FlowConfiguration has the flow's values: one that runs it as a setup flow first, the nearest the
    // project's folder first.
    private static string? SettingsOf(string flow, IReadOnlyList<TestFile> files) => files
        .Where(file => LoginSettings.Find(file.Yaml, flow)?.GetChildren().Any() == true)
        .OrderBy(file => file.SetupFlows.Contains(flow) ? 0 : 1)
        .ThenBy(file => file.Path.Count(character => character == Path.DirectorySeparatorChar))
        .ThenBy(file => file.Path, StringComparer.Ordinal)
        .Select(file => file.Path)
        .FirstOrDefault();

    // The file whose probe runs the flow first and gives its BaseUrl, the nearest the project's folder first.
    private static string? BaseUrlFileOf(string flow, IReadOnlyList<TestFile> files) => files
        .OrderBy(file => file.SetupFlows.Contains(flow) ? 0 : 1)
        .ThenBy(file => file.Path.Count(character => character == Path.DirectorySeparatorChar))
        .ThenBy(file => file.Path, StringComparer.Ordinal)
        .FirstOrDefault(file => LoginSettings.BaseUrlIn(file.Path, flow) is not null)?.Path;

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
