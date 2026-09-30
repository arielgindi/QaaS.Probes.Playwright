using Microsoft.Extensions.Configuration;
using QaaS.Framework.SDK.ContextObjects;

namespace QaaS.Playwright.Tests;

[TestFixture]
public class PlaywrightFlowProbeTests
{
    [Test]
    public void LoadAndValidateConfiguration_UnknownKey_LogsAWarning()
    {
        var logger = new ListLogger();
        var probe = new PlaywrightFlowProbe { Context = new Context { Logger = logger } };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BaseUrl"] = "http://app.test",
            ["Flow:0"] = "Login",
        }).Build();

        var errors = probe.LoadAndValidateConfiguration(configuration);

        Assert.That(errors, Is.Empty);
        Assert.That(logger.Warnings, Has.One.Contains("'Flow'"));
    }
}
