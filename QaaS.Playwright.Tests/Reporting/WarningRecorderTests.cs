using Microsoft.Extensions.Logging;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Playwright.Reporting;

namespace QaaS.Playwright.Tests.Reporting;

[TestFixture]
public class WarningRecorderTests
{
    [Test]
    public void Log_RecordsWarningsForTheSession_AndLogsEverything()
    {
        var console = new ListLogger();
        var context = new Context { Logger = console };
        var logger = new WarningRecorder(context, "Journey");

        logger.LogInformation("Navigating to {Url}", "http://app");
        logger.LogWarning("KeepOpen ignored: {Reason}", "no terminal");

        Assert.That(PlaywrightFlowResults.ReadWarnings(context, "Journey"), Is.EqualTo(new[] { "KeepOpen ignored: no terminal" }));
        Assert.That(console.Messages, Is.EqualTo(new[] { "Navigating to http://app", "KeepOpen ignored: no terminal" }));
    }
}
