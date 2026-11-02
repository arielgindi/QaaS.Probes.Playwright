using System.Runtime.CompilerServices;
using QaaS.Playwright.Recorder;
using QaaS.Playwright.Recorder.Capture;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>
/// Recordings of sample forms (MUI 5 + Formik), as the capture script reported them, and the flow
/// and YAML each must give. Set QAAS_UPDATE_GOLDEN=1 to write what the generator gives now, then read the difference
/// before committing it.
/// </summary>
[TestFixture]
public class GoldenFlowTests
{
    public static IEnumerable<string> Recordings() =>
        Directory.GetFiles(Folder(), "*.recording.json").Select(path => Path.GetFileName(path)[..^".recording.json".Length]).Order();

    [TestCaseSource(nameof(Recordings))]
    public void Recording_GivesTheGoldenFlowAndYaml(string name)
    {
        var recording = Recording.FromJson(File.ReadAllText(Path.Combine(Folder(), $"{name}.recording.json")));

        var flow = FlowCodeGenerator.Generate(recording, name, "Flows");

        var checks = string.Concat(flow.Checks.Select(check => $"line {check.Line}: {check.Message}\n"));
        Assert.Multiple(() =>
        {
            Compare(flow.Code, $"{name}.cs.txt");
            Compare(flow.Yaml, $"{name}.flow.yaml");
            Compare(checks, $"{name}.checks.txt");
        });
    }

    private static void Compare(string actual, string file)
    {
        var path = Path.Combine(Folder(), file);
        if (Environment.GetEnvironmentVariable("QAAS_UPDATE_GOLDEN") == "1") File.WriteAllText(path, actual);
        Assert.That(actual, Is.EqualTo(File.Exists(path) ? File.ReadAllText(path).Replace("\r", "") : "(no golden file)"), file);
    }

    private static string Folder([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "Golden");
}
