using System.Collections.Immutable;
using Microsoft.Extensions.Configuration;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Playwright.Tests;

[TestFixture]
public class PlaywrightFlowProbeTests
{
    private static (PlaywrightFlowProbe Probe, List<string> Problems) Load(Dictionary<string, string?> settings)
    {
        var probe = new PlaywrightFlowProbe { Context = new Context { Logger = new ListLogger() } };
        var errors = probe.LoadAndValidateConfiguration(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return (probe, [.. errors!.Select(error => error.ErrorMessage!)]);
    }

    private static Dictionary<string, string?> Mistakes() => new()
    {
        ["BaseUrl"] = "http://app.test",
        ["BrowserUrl"] = "ws://localhost:1",
        ["DefaultTimeout"] = "seven hundred",
        ["RemoteBrowserUrl"] = "ws://chrome",
        ["SetupFlows:0"] = "CheckUserFlow",
        ["FlowConfiguration:CheckUserFlow:Usr"] = "alice",
        ["Flows:0"] = "NoSuchFlow",
    };

    [Test]
    public void LoadAndValidateConfiguration_WorkingSettings_HaveNoProblems() =>
        Assert.That(Load(new() { ["BaseUrl"] = "http://app.test", ["Flows:0"] = "SubmitOrderFlow" }).Problems, Is.Empty);

    [Test]
    public void LoadAndValidateConfiguration_ReturnsEveryMistake_TheFlowsOnesToo()
    {
        var (_, problems) = Load(Mistakes());

        Assert.That(problems, Has.Count.EqualTo(4));
        Assert.That(problems, Has.One.EqualTo("DefaultTimeout: 'seven hundred' is not a whole number"));
        Assert.That(problems, Has.One.EqualTo("RemoteBrowserUrl: removed: use BrowserUrl"));
        Assert.That(problems, Has.One.EqualTo("FlowConfiguration:CheckUserFlow:Usr: not a setting (known: User)"));
        Assert.That(problems, Has.One.StartsWith("Flow 'NoSuchFlow' not found"));
    }

    [Test]
    public void LoadAndValidateConfiguration_FlowSettingsGivenAsASingleValue_IsAProblem()
    {
        var settings = new Dictionary<string, string?>
        {
            ["BaseUrl"] = "http://app.test", ["Flows:0"] = "CheckUserFlow", ["FlowConfiguration:CheckUserFlow"] = "alice",
        };

        Assert.That(Load(settings).Problems,
            Is.EqualTo(new[] { "FlowConfiguration:CheckUserFlow: expected settings, not 'alice'" }));
    }

    [TestCase("DefaultTimeout", "0", "DefaultTimeout: The field DefaultTimeout must be between 1 and 2147483647.")]
    [TestCase("Parallelism", "0", "Parallelism: The field Parallelism must be between 1 and 2147483647.")]
    public void LoadAndValidateConfiguration_ValueOutOfRange_IsAProblem(string key, string value, string problem)
    {
        // A DefaultTimeout of 0 lets an action wait forever; QaaS 4.8 would run it anyway.
        var settings = new Dictionary<string, string?>
        {
            ["BaseUrl"] = "http://app.test", ["Flows:0"] = "SubmitOrderFlow", ["ForEach"] = "Items", [key] = value,
        };

        Assert.That(Load(settings).Problems, Is.EqualTo(new[] { problem }));
    }

    [Test]
    public void Run_WithProblems_ThrowsThemAllAtOnce()
    {
        // QaaS 4.8 checks probe settings before it loads the probe, so the probe itself must fail the session.
        var (probe, problems) = Load(Mistakes());

        var failure = Assert.Throws<InvalidOperationException>(() =>
            probe.Run(ImmutableList<SessionData>.Empty, ImmutableList<DataSource>.Empty));

        Assert.That(failure!.Message, Does.StartWith("ProbeConfiguration has 4 problem(s):\n  - "));
        Assert.That(problems, Has.All.Matches<string>(problem => failure.Message.Contains(problem)));
    }
}
