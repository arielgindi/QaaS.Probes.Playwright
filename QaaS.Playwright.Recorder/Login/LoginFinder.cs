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

    /// <summary>The person chose none of the test projects found.</summary>
    public bool SaidNone { get; init; }
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
/// When nothing is found, one line says why; <c>--login-why</c> prints the whole search.
/// </summary>
internal static class LoginFinder
{
    // Words in a flow's name that say it logs in, when no test runs a setup flow.
    private static readonly string[] LoginWords = ["Login", "LogIn", "SignIn", "Auth"];

    /// <summary>
    /// The request with the login flow found for it, asked about or as remembered for <paramref name="folder"/>; the
    /// request as it is when the person said no; null when nothing was found, or the person said no before, which one
    /// dim line says.
    /// </summary>
    /// <exception cref="OperationCanceledException">The input ended at a question.</exception>
    public static RecordRequest? Offer(RecordRequest request, string folder)
    {
        if (RememberedLogin.DeclinedIn(folder) is { } no)
        {
            ConsoleUi.Note(NotLoggingInHere(no));
            return null;
        }

        var remembered = RememberedLogin.In(folder) is { Found: true } found ? found : null;
        var answered = RememberedLogin.HasAnswer(folder);
        var search = new LoginSearch();
        var discovered = Search(request.OutputDir, folder, search, interactive: true, remembered);
        if (discovered.SaidNone) return Declined(request, folder, null);
        foreach (var problem in discovered.Problems) ConsoleUi.Warning(problem);
        if (discovered.Again is { } again) return Take(request, folder, again, discovered, Told.OnlyWhatIsWrong);
        if (remembered is not null && discovered.Problems.Count == 0)
            ConsoleUi.Warning($"{remembered.Flow}, which you logged in with here before, is not found now: " +
                              $"{search.Why ?? $"it is not in {Built(discovered)}"}. Build your test project (dotnet build) " +
                              "and record again, or record with --no-login. To see where the recorder looked: qaas-playwright --login-why");
        switch (discovered.Likely)
        {
            case []:
                if (!answered && discovered.Problems.Count == 0) ConsoleUi.Note(NoLoginFlow(search));
                return null;
            case [var one]:
                return Take(request, folder, one, discovered, Told.AndAsked);
            default:
                return ChooseAmong(request, folder, discovered);
        }
    }

    /// <summary>
    /// The flow named <paramref name="flow"/> in the newest build of the test project of <paramref name="outputDir"/>, or of
    /// the current folder, with its values from the project's YAML, for <c>--login</c> given alone; else null, and why.
    /// </summary>
    public static (LoginFlow? Found, string? Why) Named(string flow, string outputDir)
    {
        var search = new LoginSearch();
        foreach (var discovered in Each(outputDir, Environment.CurrentDirectory, search))
        {
            if (discovered.Flows.FirstOrDefault(candidate => SameName(candidate.Name, flow)) is not { } found) continue;
            if (FlowCheck.CannotRun(found.Type) is { } cannot) return (null, cannot);
            return (LoginOf(found, discovered), null);
        }

        return (null, WhyNot(flow, search, outputDir));
    }

    /// <summary>
    /// What the search finds without asking anything or building: the flows of the test project here, for the questions
    /// of <c>qaas-playwright</c> run without arguments.
    /// </summary>
    public static Discovered? Discover(string outputDir) =>
        Each(outputDir, Environment.CurrentDirectory, new LoginSearch()).FirstOrDefault(found => found.Build is not null);

    /// <summary>The login flow named <paramref name="flow"/> as found in <paramref name="discovered"/>, with its values and BaseUrl.</summary>
    public static LoginFlow LoginOf(FoundFlow flow, Discovered discovered) =>
        new(flow.Name, LoadPath(flow, discovered.Build), SettingsOf(flow.Name, discovered.Files), Found: true,
            discovered.Project?.ProjectFile, BaseUrlFileOf(flow.Name, discovered.Files));

    /// <summary>The file of the tests' YAML that gives <paramref name="flow"/>'s values; null when none does.</summary>
    public static string? SettingsIn(Discovered discovered, string flow) => SettingsOf(flow, discovered.Files);

    /// <summary>
    /// Prints the whole search, for <c>--login-why</c>: the projects found, the build used, the DLLs scanned, the flows
    /// found and why each was chosen or not, and what recording here would do. Asks nothing and builds nothing.
    /// </summary>
    public static void Explain(string outputDir, string folder)
    {
        var search = new LoginSearch();
        search.Say($"Looking for the login flow of your tests, from {Path.GetFullPath(folder)}");
        search.Say(RememberedLogin.DeclinedIn(folder) is { } no ? $"Remembered here: {NotLoggingInHere(no)}"
            : RememberedLogin.In(folder) is { } login ? $"Remembered here: log in with {login.Flow}, from {ConsoleUi.Relative(login.Assembly)}" +
                                                        (login.Found ? " (found again in the newest build each time)" : " (given in the command)")
            : RememberedLogin.ProjectIn(folder) is { } project ? $"Remembered here: the test project {ConsoleUi.Relative(project)}"
            : "Remembered here: nothing yet, so the first recording here asks.", 1);
        var any = false;
        foreach (var discovered in Each(outputDir, folder, search))
        {
            any = true;
            search.Say(discovered.Likely switch
            {
                _ when discovered.Problems.Count > 0 => $"Result: no login, because: {string.Join(" ", discovered.Problems)}",
                [] => $"Result: no login flow in {discovered.Project?.Name}: {search.Why ?? "none of its flows logs in"}.",
                [var one] => $"Result: recording here offers to log in first with {one.Name}, from {ConsoleUi.Relative(one.Dll)}.",
                var several => $"Result: recording here asks which of {string.Join(", ", several.Select(flow => flow.Name))} logs in.",
            }, 1);
        }

        if (!any) search.Say($"Result: {NoLoginFlow(search)}", 1);
        ConsoleUi.LoginSearch(search);
    }

    // "Not logging in here (you said no on 2026-11-03). --login LoginFlow to log in, --no-login to ask again."
    private static string NotLoggingInHere(DeclinedLogin no) =>
        $"Not logging in here (you said no{(no.On is { } on ? $" on {on:yyyy-MM-dd}" : " before")}). " +
        $"--login {no.Flow ?? "<flow>"} to log in, --no-login to ask again.";

    private static string NoLoginFlow(LoginSearch search) =>
        $"No login flow: {search.Why ?? "none of your tests' flows logs in"}. To log in first, run the recorder in your test " +
        "project's folder, or give --login <flow>; qaas-playwright --login-why shows the search.";

    private static string WhyNot(string flow, LoginSearch search, string outputDir) =>
        $"--login {flow} needs the DLL that has {flow}, and the recorder did not find it: {search.Why ?? $"{flow} is in no build it found"}. " +
        $"Build your test project (dotnet build) and run the recorder in its folder, or add --login-assembly with that DLL, e.g. " +
        $"{Path.Combine("bin", "Debug", "net10.0", "YourShared.dll")}. To see where it looked: qaas-playwright --login-why" +
        (outputDir == "Flows" ? "" : $" --output-dir {outputDir}");

    // What the person is told of the flow found: all of it, and asked; all of it, chosen; or only what is wrong.
    private enum Told { AndAsked, Chosen, OnlyWhatIsWrong }

    // The search in each test project found: the one taken, or each of those that use QaaS, when several do and the
    // person has not chosen one; nothing asked.
    private static IEnumerable<Discovered> Each(string outputDir, string folder, LoginSearch search)
    {
        var (project, several, _) = FindProject(outputDir, folder, search, interactive: false);
        foreach (var each in project is null ? several : [project])
        {
            if (project is null) search.Say($"In {each.Name} ({ConsoleUi.Relative(each.ProjectFile)}):", 1);
            yield return SearchIn(each, search, interactive: false, remembered: null);
        }
    }

    private static Discovered Search(string outputDir, string folder, LoginSearch search, bool interactive, LoginFlow? remembered)
    {
        search.Say($"Looking for the login flow of your tests, from {Path.GetFullPath(folder)}");
        var (project, _, saidNone) = FindProject(outputDir, folder, search, interactive);
        return project is null ? new Discovered { SaidNone = saidNone } : SearchIn(project, search, interactive, remembered);
    }

    // The test project: the nearest .csproj from the output folder or the current one up; else those below the current
    // one and in its solutions that use QaaS: the one, the one chosen here before, or the one the person chooses now.
    private static (TestProject? Project, List<TestProject> Several, bool SaidNone) FindProject(string outputDir, string folder,
        LoginSearch search, bool interactive)
    {
        foreach (var from in new[] { outputDir, folder }.Select(Path.GetFullPath).Distinct())
        {
            if (TestProject.Near(from) is not { } near) continue;
            search.Say($"Test project: {ConsoleUi.Relative(near.ProjectFile)}, the nearest .csproj in {from} or a folder above it.", 1);
            return (near, [], false);
        }

        var here = Path.GetFullPath(folder);
        search.Say($"No .csproj in {here} or a folder above it: looking up to {TestProject.Depth} folders below it, and in its .sln and .slnx files.", 1);
        var below = TestProject.Below(folder);
        if (below.Count == 0)
        {
            search.None($"no test project in {here}, above it or up to {TestProject.Depth} folders below it");
            return (null, [], false);
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
                return (null, [], false);
            case [var one]:
                search.Say($"Taken: {one.Name}, the one that uses QaaS.", 1);
                return (one, [], false);
        }

        if (RememberedLogin.ProjectIn(folder) is { } kept && withQaaS.FirstOrDefault(project => Same(project.ProjectFile, kept)) is { } chosen)
        {
            search.Say($"Taken: {chosen.Name}, which you chose here before.", 1);
            return (chosen, [], false);
        }

        if (!interactive)
        {
            search.Say($"{withQaaS.Count} of them use QaaS: recording here asks which one, once, and remembers it. Each:", 1);
            return (null, withQaaS, false);
        }

        var choice = ConsoleUi.Choose(
            $"{withQaaS.Count} test projects here use QaaS. Which one has the tests whose login flow the recorder should run " +
            "first, so you start recording signed in?",
            [.. withQaaS.Select(project => $"{project.Name} ({ConsoleUi.Relative(project.ProjectFile)})"), "None: record without logging in"]);
        if (choice == withQaaS.Count) return (null, [], true);
        Remembered(RememberedLogin.RememberProject(folder, withQaaS[choice].ProjectFile));
        search.Say($"Taken: {withQaaS[choice].Name}, which you chose.", 1);
        return (withQaaS[choice], [], false);
    }

    // The project's YAML, its build (built first when the person agrees, when interactive), its flows, and the likely ones.
    private static Discovered SearchIn(TestProject project, LoginSearch search, bool interactive, LoginFlow? remembered)
    {
        var discovered = new Discovered { Project = project };
        var (files, codeChanged, projectChanged) = project.Look();
        discovered.Files = files;
        discovered.SetupIn = files.SelectMany(file => file.SetupFlows).GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(names => names.Key, names => names.Count(), StringComparer.OrdinalIgnoreCase);
        search.Say(discovered.SetupIn.Count == 0
            ? $"The YAML of {project.Name}: no file runs a setup flow, so a flow whose name says it logs in is looked for."
            : $"The YAML of {project.Name} runs setup flows: " +
              string.Join(", ", discovered.SetupIn.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key} (in {ConsoleUi.Count(pair.Value, "file")})")), 1);
        var expected = remembered?.Flow ?? discovered.SetupIn.OrderByDescending(pair => pair.Value).Select(pair => pair.Key).FirstOrDefault();
        var changed = codeChanged > projectChanged ? codeChanged : projectChanged;
        discovered.Build = interactive
            ? Built(project, expected, remembered, codeChanged, projectChanged, discovered.SetupIn, search)
            : project.NewestBuild(changed, search);
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
                                            "or record with --no-login.");
                continue;
            }

            var type = named.Single();
            flows.Add(new FoundFlow(type, string.IsNullOrEmpty(type.Assembly.Location) ? build.Dll : type.Assembly.Location,
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

    // A flow in the build's folder runs from its DLL there, as given with --login-assembly; one elsewhere, e.g. a package
    // a library's build does not copy, from the build, which loads what it uses.
    private static string LoadPath(FoundFlow flow, ProjectBuild? build) =>
        build is null || Same(Path.GetDirectoryName(flow.Dll)!, Path.GetDirectoryName(build.Dll)!) ? flow.Dll : build.Dll;

    private static string FileOf(Assembly assembly, ProjectBuild build) =>
        Path.GetFileName(string.IsNullOrEmpty(assembly.Location) ? build.Dll : assembly.Location);

    private static RecordRequest Take(RecordRequest request, string folder, FoundFlow flow, Discovered discovered, Told told)
    {
        var login = LoginOf(flow, discovered);
        var values = LoginValues.Of(flow.Type, login.Settings);
        switch (told)
        {
            case Told.AndAsked:
                ConsoleUi.FoundLogin(flow, login.Settings, values);
                if (ConsoleUi.Confirm($"Log in first with {flow.Name}? The recorder runs it before you record, so you start " +
                                      "signed in, as your tests do")) break;
                return Declined(request, folder, flow.Name);
            case Told.Chosen:
                ConsoleUi.LoginValuesOf(flow.Name, login.Settings, values);
                break;
            default:
                ConsoleUi.LoginValuesOf(flow.Name, login.Settings, values, onlyWrong: true);
                break;
        }

        Remembered(RememberedLogin.Remember(folder, login));
        return request with { Login = login, LoginRemembered = false };
    }

    private static RecordRequest ChooseAmong(RecordRequest request, string folder, Discovered discovered)
    {
        var flows = discovered.Likely;
        var choice = ConsoleUi.Choose($"Your tests have {flows.Count} flows that may log in. Which one should the recorder run " +
                                      "first, so you start recording signed in?",
            [.. flows.Select(ConsoleUi.Described), "None: record without logging in"]);
        return choice < flows.Count ? Take(request, folder, flows[choice], discovered, Told.Chosen) : Declined(request, folder, null);
    }

    private static RecordRequest Declined(RecordRequest request, string folder, string? flow)
    {
        Remembered(RememberedLogin.Decline(folder, flow));
        ConsoleUi.Info($"Recording without logging in, here from now on. To log in first later, record with --login {flow ?? "<flow>"}; " +
                       "with --no-login, the next recording asks again.");
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

    // The file whose probe runs the flow first and gives its BaseUrl, the nearest the project's folder first.
    private static string? BaseUrlFileOf(string flow, IReadOnlyList<TestFile> files) => files
        .OrderBy(file => file.SetupFlows.Contains(flow) ? 0 : 1)
        .ThenBy(file => file.Path.Count(character => character == Path.DirectorySeparatorChar))
        .ThenBy(file => file.Path, StringComparer.Ordinal)
        .FirstOrDefault(file => LoginSettings.BaseUrlIn(file.Path, flow) is not null)?.Path;

    // The newest build, built first when the person agrees, when there is none or the code changed since. Only asked
    // when the tests run a setup flow, or a login was found here before: a project without one is none of the
    // recorder's business. Null when there is no build to look in.
    private static ProjectBuild? Built(TestProject project, string? expected, LoginFlow? remembered, DateTime codeChanged,
        DateTime projectChanged, Dictionary<string, int> setupIn, LoginSearch search)
    {
        var build = project.NewestBuild(codeChanged > projectChanged ? codeChanged : projectChanged, search);
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
            ? $"{project.Name} is not built yet, so the recorder cannot run {why} to log you in first."
            : $"{project.Name} changed since it was built, {ConsoleUi.Ago(build.BuiltAt.ToLocalTime())}, so its login flow may be out of date.");
        if (!ConsoleUi.Confirm(build is null
                ? $"Build it now with {command}? Then the recorder logs you in with {expected}."
                : $"Build it again with {command}? Then the recorder logs you in with the latest {expected}."))
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
        if (exitCode == 0) return project.NewestBuild(DateTime.MinValue, search) ?? build;
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
