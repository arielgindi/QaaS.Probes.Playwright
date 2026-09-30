using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Playwright.Browser;
using QaaS.Playwright.Configuration;

namespace QaaS.Playwright.Flows;

/// <summary>
/// Runs flows in order on one page and records each one's outcome for <see cref="PlaywrightFlowAssertion"/>, named per
/// item in ForEach mode. The first flow that throws stops the run: its failure is recorded with a screenshot, the
/// page's URL and the whole exception, then rethrown.
/// </summary>
internal sealed class FlowRunner(
    Context context, string? sessionName, string? probeName, PlaywrightFlowConfig config, IConfiguration flowConfiguration)
{
    private const int ScreenshotTimeoutMs = 5_000;

    public async Task RunAsync(IEnumerable<string> flowNames, BrowserSession browser, FlowItem? item = null)
    {
        foreach (var flowName in flowNames)
        {
            var name = item?.NameOf(flowName) ?? flowName;
            var timer = Stopwatch.StartNew();
            try
            {
                await Create(flowName, item).RunAsync(browser.Page);
                Record(new PlaywrightFlowOutcome(name, Passed: true, ProbeName: probeName));
                context.Logger.LogInformation("{Flow} passed in {Seconds:0.0} s", name, timer.Elapsed.TotalSeconds);
            }
            catch (Exception failure)
            {
                context.Logger.LogWarning("{Flow} failed after {Seconds:0.0} s", name, timer.Elapsed.TotalSeconds);
                await RecordFailureAsync(name, failure, browser);
                throw;
            }
        }
    }

    // A crashed page fails whatever the flow waits for next, so the crash is the reason, not what timed out after it.
    private async Task RecordFailureAsync(string name, Exception failure, BrowserSession browser)
    {
        var (screenshot, noScreenshotReason) =
            browser.Crashed ? (null, "the page crashed") : await TryScreenshotAsync(browser.Page);
        var reason = browser.Crashed ? "The page crashed" : Describe(failure);
        var detail = noScreenshotReason is null ? $"{failure}" : $"No screenshot: {noScreenshotReason}\n{failure}";
        Record(new PlaywrightFlowOutcome(name, Passed: false, reason, screenshot, browser.Page.Url, probeName, detail));
    }

    // The message, or the exception's type when it has none, and the innermost cause, which often names the real problem.
    private static string Describe(Exception failure)
    {
        static string MessageOf(Exception exception) =>
            string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;

        var cause = failure.GetBaseException();
        return cause == failure ? MessageOf(failure) : $"{MessageOf(failure)} caused by: {MessageOf(cause)}";
    }

    // Each flow sees only its own FlowConfiguration:<FlowName> section. The probe checked every flow's settings before
    // the run, so this throws only for a flow whose own validation changed its mind.
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
                $"{flowName} has invalid settings: {string.Join("; ", errors.Select(error => error.ErrorMessage))}");
        return flow;
    }

    private void Record(PlaywrightFlowOutcome outcome) => PlaywrightFlowResults.Record(context, sessionName, outcome);

    // Best effort: a page that is gone or hung must not replace the flow's real failure; the report says why it failed.
    private async Task<(byte[]? Png, string? FailureReason)> TryScreenshotAsync(IPage page)
    {
        try
        {
            var options = new PageScreenshotOptions { FullPage = config.FullPageScreenshot, Timeout = ScreenshotTimeoutMs };
            return (await page.ScreenshotAsync(options), null);
        }
        catch (Exception failure)
        {
            context.Logger.LogWarning("Could not take a failure screenshot: {Message}", failure.Message);
            return (null, failure.Message);
        }
    }
}
