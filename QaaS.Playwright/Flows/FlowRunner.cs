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
/// item in ForEach mode. The first flow that throws stops the run: its failure is recorded with the screenshot and URL
/// of its page and of each pop-up it left open, and the whole exception, the run's secrets hidden, then rethrown. Each
/// flow's progress goes to the console; <paramref name="logger"/>, the run's, takes the warnings for the report. A flow
/// is created by its name with <paramref name="create"/>, by default <see cref="FlowDiscovery.Resolve"/>; a flow that
/// throws <see cref="FlowStoppedException"/> was stopped on purpose, and is neither recorded nor shot.
/// </summary>
internal sealed class FlowRunner(Context context, string? sessionName, string? probeName, PlaywrightFlowConfig config,
    IConfiguration flowConfiguration, Secrets secrets, ILogger logger, Func<string, IPlaywrightFlow>? create = null)
{
    private const int ScreenshotTimeoutMs = 5_000;

    public async Task RunAsync(IEnumerable<string> flowNames, BrowserSession browser, FlowItem? item = null)
    {
        foreach (var flowName in flowNames)
        {
            var name = item?.NameOf(flowName) ?? flowName;
            var timer = Stopwatch.StartNew();
            var pagesBefore = browser.Pages.Count;
            try
            {
                await RunFlowAsync(flowName, item, browser.Page);
                Record(new PlaywrightFlowOutcome(name, Passed: true, ProbeName: probeName));
                context.Logger.LogInformation("{Flow} passed in {Seconds:0.0} s", name, timer.Elapsed.TotalSeconds);
            }
            catch (Exception failure) when (failure is not FlowStoppedException)
            {
                context.Logger.LogWarning("{Flow} failed after {Seconds:0.0} s", name, timer.Elapsed.TotalSeconds);
                var popups = browser.Pages.Skip(pagesBefore).Where(page => !page.IsClosed);
                await RecordFailureAsync(name, failure, browser, popups);
                throw;
            }
        }
    }

    // A failed call does not say which page it was on, so the failure is shown on the flow's page, with each pop-up the
    // flow left open. A crashed page fails whatever the flow waits for next, so the crash is the reason, not what timed
    // out after it.
    private async Task RecordFailureAsync(
        string name, Exception failure, BrowserSession browser, IEnumerable<IPage> popups)
    {
        var (screenshot, noScreenshotReason) = await CaptureAsync(browser, browser.Page);
        List<string> notes = noScreenshotReason is null ? [] : [$"No screenshot: {noScreenshotReason}"];
        var captured = new List<PlaywrightFlowPopup>();
        foreach (var popup in popups)
        {
            var (png, noPngReason) = await CaptureAsync(browser, popup);
            captured.Add(new PlaywrightFlowPopup(secrets.Scrub(popup.Url), png));
            if (noPngReason is not null) notes.Add($"No screenshot of pop-up {captured.Count}: {noPngReason}");
        }

        var reason = browser.HasCrashed(browser.Page) ? "The page crashed" : Describe(failure);
        var detail = string.Join('\n', notes.Append($"{failure}"));
        Record(new PlaywrightFlowOutcome(name, Passed: false, secrets.Scrub(reason), screenshot,
            secrets.Scrub(browser.Page.Url), probeName, secrets.Scrub(detail), captured));
    }

    private async Task<(byte[]? Png, string? FailureReason)> CaptureAsync(BrowserSession browser, IPage page) =>
        browser.HasCrashed(page) ? (null, "the page crashed") : await TryScreenshotAsync(page);

    // The message, or the exception's type when it has none, and the innermost cause, which often names the real problem.
    // The recorder tells a login flow's failure the same way.
    internal static string Describe(Exception failure)
    {
        static string MessageOf(Exception exception) =>
            string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;

        var cause = failure.GetBaseException();
        return cause == failure ? MessageOf(failure) : $"{MessageOf(failure)} caused by: {MessageOf(cause)}";
    }

    // Each flow sees only its own FlowConfiguration:<FlowName> section. The probe checked every flow's settings before
    // the run, so invalid settings fail only a flow whose own validation changed its mind.
    private async Task RunFlowAsync(string flowName, FlowItem? item, IPage page)
    {
        var (flow, problems) = FlowLoader.Load(
            create?.Invoke(flowName) ?? FlowDiscovery.Resolve(flowName), flowName, context, config.BaseUrl, flowConfiguration, item);
        try
        {
            if (problems.Count > 0)
                throw new InvalidOperationException($"{flowName} has invalid settings: {string.Join("; ", problems)}");
            await flow.RunAsync(page);
        }
        catch
        {
            await DisposeAfterFailureAsync(flow, item?.NameOf(flowName) ?? flowName);
            throw;
        }

        await FlowLoader.DisposeAsync(flow);
    }

    // A teardown error must not hide the flow's own failure, so it only warns.
    private async Task DisposeAfterFailureAsync(IPlaywrightFlow flow, string name)
    {
        try
        {
            await FlowLoader.DisposeAsync(flow);
        }
        catch (Exception failure)
        {
            logger.LogWarning(failure, "Could not dispose {Flow} after it failed: {Message}", name, failure.Message);
        }
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
