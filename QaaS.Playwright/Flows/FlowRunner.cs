using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using QaaS.Framework.SDK.ContextObjects;

namespace QaaS.Playwright.Flows;

/// <summary>
/// Runs flows in order on one page and records each one's outcome for <see cref="PlaywrightFlowAssertion"/>.
/// The first flow that throws stops the run: its failure is recorded with a screenshot, then rethrown.
/// </summary>
internal sealed class FlowRunner(
    Context context, string sessionName, string baseUrl, IConfiguration flowConfiguration, bool fullPageScreenshot)
{
    private const int ScreenshotTimeoutMs = 5_000;

    public async Task RunAsync(IEnumerable<string> flowNames, IPage page, string label)
    {
        foreach (var flowName in flowNames)
        {
            context.Logger.LogInformation("{Label}: {FlowName}", label, flowName);
            try
            {
                await Create(flowName).RunAsync(page);
                Record(new PlaywrightFlowOutcome(flowName, Passed: true));
            }
            catch (Exception failure)
            {
                Record(new PlaywrightFlowOutcome(flowName, Passed: false, failure.Message, await TryScreenshotAsync(page)));
                throw;
            }
        }
    }

    // Each flow gets only its own FlowConfiguration:<FlowName> section, never its siblings' keys.
    private IPlaywrightFlow Create(string flowName)
    {
        var flow = FlowDiscovery.Resolve(flowName);
        flow.Context = context;
        flow.BaseUrl = baseUrl;

        var errors = flow.LoadAndValidateConfiguration(flowConfiguration.GetSection(flowName));
        if (errors is { Count: > 0 })
            throw new InvalidOperationException(
                $"Flow '{flowName}' configuration is invalid: {string.Join("; ", errors.Select(error => error.ErrorMessage))}");
        return flow;
    }

    private void Record(PlaywrightFlowOutcome outcome) => PlaywrightFlowResults.Record(context, sessionName, outcome);

    // Best effort: a page that is gone or hung must not replace the flow's real failure.
    private async Task<byte[]?> TryScreenshotAsync(IPage page)
    {
        try
        {
            return await page.ScreenshotAsync(new PageScreenshotOptions
            {
                FullPage = fullPageScreenshot,
                Timeout = ScreenshotTimeoutMs,
            });
        }
        catch (Exception failure)
        {
            context.Logger.LogWarning("Could not capture a failure screenshot: {Message}", failure.Message);
            return null;
        }
    }
}
