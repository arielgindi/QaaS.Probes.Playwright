using Microsoft.Extensions.Configuration;
using QaaS.Playwright.Configuration;

namespace QaaS.Playwright.Tests.Configuration;

[TestFixture]
public class ProbeRulesTests
{
    private static List<string> Broken(PlaywrightFlowConfig config, Dictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build();
        return [.. ProbeRules.Broken(configuration, config).Select(problem => $"{problem}")];
    }

    private static PlaywrightFlowConfig ForEach(string[]? flows = null) =>
        new() { ForEach = "Missions", Flows = flows ?? ["CreateMission"] };

    [Test]
    public void Broken_WorkingSettings_HaveNoProblems()
    {
        Assert.That(Broken(new PlaywrightFlowConfig { Flows = ["Login"] }), Is.Empty);
        Assert.That(Broken(ForEach()), Is.Empty);
    }

    [Test]
    public void Removed_RemovedSettings_SayWhatToDoInstead()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["remoteBrowserUrl"] = "ws://chrome", ["DisableAnimations"] = "true", ["BaseUrl"] = "http://app",
        }).Build();

        Assert.That(ProbeRules.Removed(configuration).Select(problem => $"{problem}"), Is.EqualTo(new[]
        {
            "DisableAnimations: removed: it never took effect, so delete it", "remoteBrowserUrl: removed: use BrowserUrl",
        }));
    }

    [Test]
    public void Broken_NoFlowAtAll_IsAProblem() =>
        Assert.That(Broken(new PlaywrightFlowConfig()), Is.EqualTo(new[] { "Flows: nothing to run: Flows and SetupFlows are both empty" }));

    [Test]
    public void Broken_EmptyFlowName_IsAProblem() =>
        // A YAML null in a list binds as "".
        Assert.That(Broken(new PlaywrightFlowConfig { Flows = ["Login", "", "Logout"] }),
            Is.EqualTo(new[] { "Flows:1: empty flow name" }));

    [Test]
    public void Broken_ForEachWithOnlySetupFlows_IsAProblem() =>
        Assert.That(Broken(new PlaywrightFlowConfig { ForEach = "Missions", SetupFlows = ["Login"] }),
            Is.EqualTo(new[] { "Flows: empty, so ForEach has nothing to run for each item" }));

    [Test]
    public void Broken_ParallelismWithoutForEach_IsAProblem() =>
        Assert.That(Broken(new PlaywrightFlowConfig { Flows = ["Login"], Parallelism = 4 }),
            Is.EqualTo(new[] { "Parallelism: applies only with ForEach" }));

    [Test]
    public void Broken_SettingsThatNeedOneRunWithForEach_AreProblems()
    {
        var config = ForEach();
        config.SaveStorageStatePath = "state.json";
        config.IsolateContext = false;

        Assert.That(Broken(config), Is.EqualTo(new[]
        {
            "SaveStorageStatePath: cannot be used with ForEach, where several workers run",
            "IsolateContext: must stay true with ForEach: every worker needs a browser context of its own",
        }));
    }

    [Test]
    public void Broken_FlowConfigurationOfAFlowThatDoesNotRun_IsAProblem()
    {
        var settings = new Dictionary<string, string?> { ["FlowConfiguration:Logn:User"] = "a", ["FlowConfiguration:login:User"] = "b" };

        Assert.That(Broken(new PlaywrightFlowConfig { SetupFlows = ["Login"] }, settings),
            Is.EqualTo(new[] { "FlowConfiguration:Logn: no flow named 'Logn' is in SetupFlows or Flows" }));
    }
}
