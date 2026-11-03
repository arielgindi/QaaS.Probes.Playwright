using System.Runtime.CompilerServices;
using System.Text.Json;
using QaaS.Playwright.Recorder;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Login;
using QaaS.Playwright.Recorder.TestRun;
using static QaaS.Playwright.Tests.Recorder.Captured;
using CapturedRecording = QaaS.Playwright.Recorder.Capture.Recording;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>
/// Every kind of flow the recorder writes must build in a test project as strict as a team's: the golden recordings,
/// with and without auto checks, the person's checks and edits among them, and the steps that read the flow's own members (BaseUrl, settings) or none. Its YAML
/// must then bind to its settings as QaaS reads a test's, lists of a loop included.
/// </summary>
[TestFixture]
[Category("EndToEnd")]
public class GeneratedFlowBuildTests
{
    private string _folder = null!;

    [SetUp]
    public void CreateFolder() => _folder = Directory.CreateTempSubdirectory("qaas-recorder-build-").FullName;

    [TearDown]
    public void DeleteFolder() => Directory.Delete(_folder, recursive: true);

    [Test]
    public async Task EveryGeneratedFlow_Builds_WithoutWarnings_AndItsYamlBindsToItsSettings()
    {
        await FlowCompiler.CreateProjectAsync(_folder, "GeneratedFlows");
        var flows = FlowCompiler.FlowsFolder(_folder);
        var generated = new Dictionary<string, string>();
        foreach (var (name, recording, _) in Recordings())
        {
            var flow = FlowCodeGenerator.Generate(recording, name, ProjectNamespace.Of(flows));
            await File.WriteAllTextAsync(Path.Combine(flows, $"{name}.cs"), flow.Code);
            generated[name] = flow.Yaml;
        }

        var withoutChecks = new List<string>();
        foreach (var (name, recording, _) in Recordings().Where(sample => sample.Golden))
        {
            var flow = FlowCodeGenerator.Generate(recording, $"{name}Unchecked", ProjectNamespace.Of(flows), autoChecks: false);
            await File.WriteAllTextAsync(Path.Combine(flows, $"{name}Unchecked.cs"), flow.Code);
            withoutChecks.Add(flow.Code);
        }

        await FlowCompiler.BuildAsync(_folder);

        var assembly = FlowAssemblyContext.Load(Path.Combine(_folder, "out", "GeneratedFlows.dll"));
        Assert.Multiple(() =>
        {
            Assert.That(withoutChecks, Has.All.Not.Contain("// auto check"), "without auto checks, a flow checks only what the person chose");
            Assert.That(withoutChecks, Has.Some.Contain("Expect(").And.Contain("// check"));
            foreach (var (name, yaml) in generated)
                Assert.That(Bind(assembly.GetTypes().Single(type => type.Name == name), yaml), Is.EqualTo(Defaults(assembly, name)),
                    $"{name}: its YAML binds to the values it recorded");
            // A list of fewer items than recorded is that list, not the recorded one with the first items replaced.
            Assert.That(Bind(assembly.GetTypes().Single(type => type.Name == "AddOrderLines"), """
                FlowConfiguration:
                  AddOrderLines:
                    Lines:
                      - Product: 'Stapler'
                        Quantity: 3
                """), Does.Contain("""
                "Lines":[{"Product":"Stapler","Quantity":3}]
                """.Trim()));
        });
    }

    [Test]
    public async Task TestBuild_CompilesEveryGeneratedFlow_AsDotnetBuildDoes_AndTellsTheSameErrorOfItsCode()
    {
        using var direct = new FlowBuild();
        using var dotnetBuild = new FlowBuild(direct: false);
        var flows = Recordings().Select(sample => FlowCodeGenerator.Generate(sample.Recording, sample.Name, "Flows"))
            .Concat(Recordings().Where(sample => sample.Golden)
                .Select(sample => FlowCodeGenerator.Generate(sample.Recording, sample.Name, "Flows", autoChecks: false)))
            .ToList();
        var warnings = new List<(string Flow, IReadOnlyList<string> Compiled, IReadOnlyList<string> Built)>();
        foreach (var flow in flows)
            warnings.Add((flow.Code, (await direct.BuildAsync(flow, CancellationToken.None)).Warnings,
                (await dotnetBuild.BuildAsync(flow, CancellationToken.None)).Warnings));

        // Code a person could not write in the panel: one more line that warns, or does not build.
        var sample = flows[0];
        var warned = sample with { Code = sample.Code + "\nfile static class Warned { public static int Use(string? text) => text.Length; }" };
        warnings.Add((warned.Code, (await direct.BuildAsync(warned, CancellationToken.None)).Warnings,
            (await dotnetBuild.BuildAsync(warned, CancellationToken.None)).Warnings));
        var broken = sample with { Code = sample.Code + "\nfile static class Broken { public static void Use() => Missing(); }" };
        var line = broken.Code.Split('\n').Length;
        var compiled = Assert.ThrowsAsync<FlowBuildException>(() => direct.BuildAsync(broken, CancellationToken.None))!;
        var built = Assert.ThrowsAsync<FlowBuildException>(() => dotnetBuild.BuildAsync(broken, CancellationToken.None))!;
        Assert.Multiple(() =>
        {
            Assert.That(direct.Direct, Is.True, "it compiled with the SDK's compiler itself");
            Assert.That(warnings[^1].Compiled, Has.One.StartWith($"line {line}: CS8602:"), "the line that warns");
            foreach (var (flow, inCompiled, inBuilt) in warnings)
                Assert.That(inCompiled, Is.EqualTo(inBuilt), $"the same warnings as dotnet build gives:\n{flow}");
            Assert.That(compiled.Message, Is.EqualTo($"The flow does not build, line {line}: CS0103: The name 'Missing' does not exist in the current context"));
            Assert.That((compiled.Message, compiled.Detail), Is.EqualTo((built.Message, built.Detail)));
        });
    }

    // The flow's settings as JSON once bound from the YAML as QaaS reads a test's, or its problems; a flow of no
    // settings has an empty section, which reads as none.
    private string Bind(Type type, string yaml)
    {
        var path = Path.Combine(_folder, $"{type.Name}.flow.yaml");
        File.WriteAllText(path, yaml);
        var flow = (IPlaywrightFlow)Activator.CreateInstance(type)!;
        var settings = LoginSettings.Read(path, type.Name);
        var problems = flow.LoadAndValidateConfiguration(settings.GetSection(type.Name));
        return problems is { Count: > 0 }
            ? string.Join("; ", problems.Select(problem => problem.ErrorMessage))
            : JsonSerializer.Serialize(type.GetProperty("Configuration")!.GetValue(flow));
    }

    private static string Defaults(System.Reflection.Assembly assembly, string name)
    {
        var type = assembly.GetTypes().Single(type => type.Name == name);
        return JsonSerializer.Serialize(type.GetProperty("Configuration")!.GetValue(Activator.CreateInstance(type)));
    }

    private static IEnumerable<(string Name, CapturedRecording Recording, bool Golden)> Recordings()
    {
        foreach (var path in Directory.GetFiles(GoldenFolder(), "*.recording.json"))
            yield return (Path.GetFileName(path)[..^".recording.json".Length], CapturedRecording.FromJson(File.ReadAllText(path)), true);

        var typed = Action("navigate", null);
        typed.Url = "https://app.test/start/appointments";
        yield return ("TypedAddressThenDialog", Recording(typed,
            Action("click", Element("button", "New", Role("button", "New")), effects: Opened("New"))), false);

        // A dialog already open when the recording starts, or whose opening click was deleted: the flow's first step is
        // in a dialog no step opened, so RunAsync finds it.
        yield return ("DialogOpenAtStart", Recording(
            Action("fill", Element("textbox", "Name", Css("name", "[name='name']", "name", dialog: "Welcome")), "Dana", dialog: "Welcome"),
            Action("click", Element("button", "Start", Role("button", "Start", dialog: "Welcome")), dialog: "Welcome",
                effects: Closed("Welcome"))), false);

        var otherSite = Action("navigate", null);
        otherSite.Url = "https://other.test/";
        yield return ("TypedAddressOnAnotherSite", Recording(otherSite), false);

        var tab = Action("newTab", null);
        tab.Page = "page1";
        tab.Url = "https://app.test/start/help";
        var search = Action("fill", Element("textbox", "Search", Css("name", "[name='q']", "q")), "x");
        search.Page = "page1";
        yield return ("TabOpenedByHand", Recording(tab, search), false);

        var confirm = Action("click", Element("button", "Delete", Role("button", "Delete")),
            effects: new CapturedEffect { Kind = "jsDialog", Text = "Delete it?", Page = "page" });
        yield return ("BrowserDialog", Recording(confirm), false);

        yield return ("HashRoute", Recording(Action("click", Element("link", "Order", Role("link", "Order")),
            effects: UrlChanged("https://app.test/#/orders/5"))), false);

        var radio = Element("radio", "Yes", Role("radio", "Yes", count: 2), Css("nameValue", "[name='insured'][value='yes']", "insured"));
        radio.Name = "insured";
        radio.Value = "yes";
        yield return ("RadioByValue", Recording(Action("check", radio, widget: "radio")), false);

        var customer = Element("textbox", "Customer", Css("name", "[name='customer']", "customer"));
        yield return ("SearchWhileBusy", Recording(Action("fill", customer, "Northwind"),
            Action("click", Element("button", "Search", Role("button", "Search")), effects: [Busy(), Busy("ariaBusy")])), false);

        // Two elements of one row: the row is a local.
        var ticket = new CapturedRow { Key = "TCK-1041", Header = "Ticket" };
        CapturedTarget InRow(string role, string name) => new()
        {
            Tag = role, Role = role, Text = name, Interactive = true, Row = ticket,
            Candidates =
            [
                new CapturedCandidate
                {
                    Kind = "row", Count = 1, Matches = true,
                    Parts =
                    [
                        new() { M = "role", Role = "row" }, new() { M = "filter", HasText = ticket.Key },
                        new() { M = "role", Role = role, Name = name, Exact = true },
                    ],
                },
            ],
        };
        yield return ("DeleteTicket", Recording(Action("check", InRow("checkbox", "Select"), widget: "checkbox"),
            Action("click", InRow("button", "Delete"), effects: Alert("Ticket deleted"))), false);

        // A loop over lines of two values, a whole and a decimal quantity among them, then a click repeated with none.
        var product = Element("textbox", "Product", Css("name", "[name='product']", "product"));
        var quantity = Element("textbox", "Quantity", Css("name", "[name='quantity']", "quantity"));
        quantity.Type = "number";
        var addLine = Element("button", "Add line", Role("button", "Add line"));
        var addEmpty = Element("button", "Add empty line", Role("button", "Add empty line"));
        CapturedAction[] lines =
        [
            .. new[] { ("Desk lamp", "2"), ("Office chair", "1.5"), ("Paper, A4", "10") }.SelectMany(line => new[]
            {
                Action("fill", product, line.Item1), Action("fill", quantity, line.Item2), Action("click", addLine),
            }),
            Action("click", addEmpty), Action("click", addEmpty), Action("click", addEmpty),
            Action("click", Element("button", "Place order", Role("button", "Place order")), effects: Alert("Order placed")),
        ];
        yield return ("AddOrderLines", Recording(lines), false);
    }

    private static string GoldenFolder([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "Golden");
}
