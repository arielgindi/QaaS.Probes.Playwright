using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Playwright.Configuration;

namespace QaaS.Playwright.Flows;

/// <summary>
/// Runs flows in order on one page and records each one's outcome for <see cref="PlaywrightFlowAssertion"/>, named per
/// item in ForEach mode. The first flow that throws stops the run: its failure is recorded with a screenshot and the
/// page's URL, then rethrown.
/// </summary>
internal sealed class FlowRunner(
    Context context, string sessionName, string? probeName, PlaywrightFlowConfig config, IConfiguration flowConfiguration)
{
    private const int ScreenshotTimeoutMs = 5_000;

    public async Task RunAsync(IEnumerable<string> flowNames, IPage page, FlowItem? item = null)
    {
        foreach (var flowName in flowNames)
        {
            var name = item?.NameOf(flowName) ?? flowName;
            var timer = Stopwatch.StartNew();
            try
            {
                await Create(flowName, item).RunAsync(page);
                Record(new PlaywrightFlowOutcome(name, Passed: true, ProbeName: probeName));
                context.Logger.LogInformation("{Flow} passed in {Seconds:0.0} s", name, timer.Elapsed.TotalSeconds);
            }
            catch (Exception failure)
            {
                context.Logger.LogWarning("{Flow} failed after {Seconds:0.0} s", name, timer.Elapsed.TotalSeconds);
                var screenshot = await TryScreenshotAsync(page);
                Record(new PlaywrightFlowOutcome(
                    name, Passed: false, failure.Message, screenshot, page.Url, probeName));
                throw;
            }
        }
    }

    // Each flow sees only its own FlowConfiguration:<FlowName> section.
    private IPlaywrightFlow Create(string flowName, FlowItem? item)
    {
        var flow = FlowDiscovery.Resolve(flowName);
        flow.Context = context;
        flow.BaseUrl = config.BaseUrl;
        flow.Item = item?.Data;
        flow.ItemIndex = item?.Index;

        var errors = flow.LoadAndValidateConfiguration(flowConfiguration.GetSection(flowName));
        if (errors is { Count: > 0 })
            throw new InvalidOperationException(
                $"FlowConfiguration:{flowName} is invalid: {string.Join("; ", errors.Select(error => error.ErrorMessage))}");
        return flow;
    }

    private void Record(PlaywrightFlowOutcome outcome) => PlaywrightFlowResults.Record(context, sessionName, outcome);

    // Best effort: a page that is gone or hung must not replace the flow's real failure.
    private async Task<byte[]?> TryScreenshotAsync(IPage page)
    {
        try
        {
            return await page.ScreenshotAsync(new() { FullPage = config.FullPageScreenshot, Timeout = ScreenshotTimeoutMs });
        }
        catch (Exception failure)
        {
            context.Logger.LogWarning("Could not take a failure screenshot: {Message}", failure.Message);
            return null;
        }
    }
}
