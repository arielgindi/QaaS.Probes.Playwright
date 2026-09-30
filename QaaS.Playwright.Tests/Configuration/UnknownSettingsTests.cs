using Microsoft.Extensions.Configuration;
using QaaS.Playwright.Configuration;

namespace QaaS.Playwright.Tests.Configuration;

[TestFixture]
public class UnknownSettingsTests
{
    private static List<string> Find(Dictionary<string, string?> settings, params string[] flows)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return [.. UnknownSettings.Find(configuration, new PlaywrightFlowConfig { Flows = flows })];
    }

    private static List<string> FindFor(PlaywrightFlowConfig config)
    {
        return [.. UnknownSettings.Find(new ConfigurationBuilder().Build(), config)];
    }

    [Test]
    public void Find_KnownKeysInAnyCase_ReportsNothing() =>
        Assert.That(Find(new() { ["BaseUrl"] = "http://app", ["flows:0"] = "Login", ["HEADLESS"] = "true" }), Is.Empty);

    [Test]
    public void Find_MisspelledKey_ReportsIt()
    {
        var warnings = Find(new() { ["Flow:0"] = "Login" });

        Assert.That(warnings, Has.Count.EqualTo(1));
        Assert.That(warnings[0], Does.Contain("'Flow'").And.Contains("Flows"), "names the key and lists the known ones");
    }

    [Test]
    public void Find_FlowConfigurationOfAFlowThatRuns_ReportsNothing() =>
        Assert.That(Find(new() { ["FlowConfiguration:login:User"] = "alice" }, "Login"), Is.Empty);

    [Test]
    public void Find_FlowConfigurationOfAFlowThatDoesNotRun_ReportsIt()
    {
        var warnings = Find(new() { ["FlowConfiguration:Logn:User"] = "alice" }, "Login");

        Assert.That(warnings, Has.Count.EqualTo(1));
        Assert.That(warnings[0], Does.Contain("FlowConfiguration:Logn"));
    }

    [Test]
    public void Find_ParallelismWithoutForEach_ReportsIt() =>
        Assert.That(FindFor(new PlaywrightFlowConfig { Parallelism = 4 }), Has.One.Contains("Parallelism is ignored"));

    [Test]
    public void Find_SaveStorageStateWithForEach_ReportsIt() =>
        Assert.That(FindFor(new PlaywrightFlowConfig { ForEach = "Missions", SaveStorageStatePath = "state.json" }),
            Has.One.Contains("SaveStorageStatePath and KeepOpen are ignored with ForEach"));

    [Test]
    public void Find_FlowConfigurationOfASetupFlow_ReportsNothing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FlowConfiguration:Login:User"] = "alice" })
            .Build();

        Assert.That(UnknownSettings.Find(configuration, new PlaywrightFlowConfig { SetupFlows = ["Login"] }), Is.Empty);
    }
}
