using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using QaaS.Playwright.Browser;
using QaaS.Playwright.Configuration;

namespace QaaS.Playwright.Flows;

/// <summary>
/// Runs Flows once per ForEach item. Each of up to Parallelism workers opens a browser context of its own, runs
/// SetupFlows once, then takes the next item from a shared queue until none is left, so a slow item holds up no other.
/// </summary>
internal sealed class ForEachRun(FlowRunner runner, PlaywrightFlowConfig config, ILogger logger)
{
    private readonly ConcurrentBag<int> _failedItems = [];

    /// <exception cref="InvalidOperationException">
    /// There was no item, an item failed, or a worker stopped; the message says which.
    /// </exception>
    public async Task RunAsync(IReadOnlyList<FlowItem> items, string[] setupFlows, string[] flows)
    {
        // Nothing would run, and nothing be verified.
        if (items.Count == 0)
            throw new InvalidOperationException($"ForEach {config.ForEach}: the DataSource produced no items.");

        var queue = new ConcurrentQueue<FlowItem>(items);
        var workerCount = Math.Min(config.Parallelism, items.Count);
        logger.LogInformation("ForEach {DataSource}: {Items} items, {Workers} workers", config.ForEach, items.Count,
            workerCount);

        var stops = await Task.WhenAll(
            Enumerable.Range(1, workerCount).Select(worker => RunWorkerAsync(worker, queue, setupFlows, flows)));

        var failed = _failedItems.Order().ToList();
        var stopped = stops.OfType<string>().ToList();
        string?[] problems =
        [
            failed.Count > 0 ? $"{failed.Count} of {items.Count} items failed: {string.Join(", ", failed)}" : null,
            stopped.Count > 0 ? $"{stopped.Count} of {workerCount} workers stopped, the first because: {stopped[0]}" : null,
            queue.IsEmpty ? null : $"{queue.Count} items did not run",
        ];
        if (problems.Any(problem => problem is not null))
            throw new InvalidOperationException(
                $"ForEach {config.ForEach}: {string.Join("; ", problems.OfType<string>())}.");
    }

    // Returns why the worker stopped early: it could not open its page or BaseUrl, run SetupFlows, or go back to BaseUrl
    // after a failed item. Null when it ran until the queue was empty.
    private async Task<string?> RunWorkerAsync(int worker, ConcurrentQueue<FlowItem> queue, string[] setupFlows,
        string[] flows)
    {
        try
        {
            await using var browser = await BrowserSession.OpenAsync(config, logger);
            await browser.OpenBaseUrlAsync(config.BaseUrl);
            await runner.RunAsync(setupFlows, browser.Page);
            while (queue.TryDequeue(out var item))
            {
                try
                {
                    await runner.RunAsync(flows, browser.Page, item);
                }
                catch
                {
                    // Recorded by the runner; the next item starts where every item starts.
                    _failedItems.Add(item.Index);
                    await browser.OpenBaseUrlAsync(config.BaseUrl);
                }
            }

            return null;
        }
        catch (Exception failure)
        {
            // The other workers take this worker's items.
            var reason = failure.Message.Split('\n')[0].TrimEnd('.');
            logger.LogWarning("ForEach worker {Worker} stopped: {Reason}", worker, reason);
            return reason;
        }
    }
}
