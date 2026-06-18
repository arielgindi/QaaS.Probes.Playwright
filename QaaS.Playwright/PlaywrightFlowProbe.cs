using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Probe;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Playwright.Configuration;
using QaaS.Playwright.Engine;

namespace QaaS.Playwright;

/// <summary>
/// Runs Playwright browser flows against either a cluster Chromium (the default) or a local Chrome on the
/// developer's machine (when <c>ENV=local</c> or <c>Headless=false</c>). Local mode auto-launches Chrome if it
/// is not already running, so subsequent runs reuse it.
///
/// All flows run in order on one shared page, so cookies and session state carry across them. Each flow's
/// outcome is published — keyed by session — through <see cref="PlaywrightFlowResults"/> so a paired
/// <c>PlaywrightFlowAssertion</c> can report a granular pass/fail. A failing flow re-throws, which both stops
/// the journey and lets the runner record a session failure.
/// </summary>
public sealed class PlaywrightFlowProbe : BaseProbe<PlaywrightFlowConfig>
{
    /// <summary>Upper bound on the best-effort failure screenshot, so a hung page cannot stall the run.</summary>
    private const int FailureScreenshotTimeoutMs = 5_000;

    /// <summary>The Activity baggage key the runner uses to publish the current session name to probes.</summary>
    private const string SessionNameBaggageKey = "qaas.probe.session-name";

    private const string FlowConfigurationSection = "FlowConfiguration";

    // The probe also needs the raw configuration to read the sibling FlowConfiguration section, which the
    // strongly-typed PlaywrightFlowConfig does not cover.
    private IConfiguration _rawConfiguration = null!;

    // Unknown keys at the probe level are tolerated because the sibling FlowConfiguration section lives next to
    // the bound PlaywrightFlowConfig keys.
    protected override BinderOptions GetConfigurationBinderOptions() =>
        new() { ErrorOnUnknownConfiguration = false };

    public override List<ValidationResult>? LoadAndValidateConfiguration(IConfiguration configuration)
    {
        _rawConfiguration = configuration;
        return base.LoadAndValidateConfiguration(configuration);
    }

    // This probe drives a browser from its own configuration and the shared Context; it does not consume the
    // session or data-source inputs. IProbe.Run is synchronous, so the async work is bridged here — the one
    // sanctioned sync-over-async point, dictated by the hook contract.
    public override void Run(IImmutableList<SessionData> sessions, IImmutableList<DataSource> dataSources) =>
        Task.Run(RunAsync).GetAwaiter().GetResult();

    private async Task RunAsync()
    {
        var setupFlowNames = Configuration.SetupFlows ?? [];
        var mainFlowNames = Configuration.Flows ?? [];
        if (setupFlowNames.Length == 0 && mainFlowNames.Length == 0)
        {
            Context.Logger.LogWarning("No flows configured; nothing to run.");
            return;
        }

        var sessionName = CurrentSessionName();
        var keepBrowserOpenForInspection =
            !Configuration.Headless && Configuration.KeepOpen && IsInteractiveConsole();

        var stopwatch = Stopwatch.StartNew();
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await new PlaywrightBrowserConnector(Context.Logger).ConnectAsync(playwright, Configuration);

        // Reuse the browser's existing default context (the user's cookies/sessions/extensions live there); only
        // create — and therefore own/dispose — a context when the browser has none.
        var existingContext = browser.Contexts.Count > 0 ? browser.Contexts[0] : null;
        var browserContext = existingContext ?? await browser.NewContextAsync();
        var ownsBrowserContext = existingContext is null;

        IPage? page = null;
        try
        {
            page = await browserContext.NewPageAsync();
            page.SetDefaultTimeout(Configuration.DefaultTimeout);
            // Pin the display size from configuration so the run and its screenshots render at a known viewport.
            await TrySetViewportAsync(page);
            await ApplyHeadlessOptimizations(page);

            Context.Logger.LogInformation("Navigating to {BaseUrl}", Configuration.BaseUrl);
            await page.GotoAsync(Configuration.BaseUrl);

            var flowConfiguration = _rawConfiguration.GetSection(FlowConfigurationSection);
            await RunFlows(setupFlowNames, flowConfiguration, page, sessionName, label: "Setup");
            await RunFlows(mainFlowNames, flowConfiguration, page, sessionName, label: "Running");

            Context.Logger.LogInformation("Done — {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            await MaybePauseForInspection(page, keepBrowserOpenForInspection);
        }
        finally
        {
            // Keep the page only when it was actually held open for interactive inspection; otherwise always close
            // it so reused Chrome instances do not accumulate orphaned tabs. Each teardown step is guarded so a
            // cleanup error never masks the real flow failure or skips the remaining teardown.
            if (page is not null && !keepBrowserOpenForInspection)
                await SafeTeardownAsync("close page", () => new ValueTask(page.CloseAsync()));
            if (ownsBrowserContext)
                await SafeTeardownAsync("dispose browser context", browserContext.DisposeAsync);
        }
    }

    private async Task MaybePauseForInspection(IPage page, bool keepBrowserOpenForInspection)
    {
        if (keepBrowserOpenForInspection)
        {
            Context.Logger.LogInformation("Browser staying open. Close the inspector to continue.");
            await page.PauseAsync();
        }
        else if (Configuration.KeepOpen)
        {
            Context.Logger.LogWarning(
                "KeepOpen=true ignored — running headless or non-interactively (e.g. CI), where PauseAsync would " +
                "hang forever.");
        }
    }

    /// <summary>Runs each named flow in order, recording its outcome; re-throws on the first failure.</summary>
    private async Task RunFlows(
        string[] flowNames, IConfiguration flowConfiguration, IPage page, string sessionName, string label)
    {
        foreach (var flowName in flowNames)
        {
            Context.Logger.LogInformation("{Label}: {FlowName}", label, flowName);
            try
            {
                await ResolveAndConfigure(flowName, flowConfiguration).RunAsync(page);
                PlaywrightFlowResults.Record(Context, sessionName, new PlaywrightFlowOutcome(flowName, Passed: true));
            }
            catch (Exception flowFailure)
            {
                // Record the failure (with a screenshot) before unwinding, so the assertion can name the failed
                // flow and show visual evidence. Re-throw so the journey stops and the runner records the failure.
                var screenshot = await TryCaptureFailureScreenshot(page);
                PlaywrightFlowResults.Record(Context, sessionName,
                    new PlaywrightFlowOutcome(flowName, Passed: false, flowFailure.Message, screenshot));
                throw;
            }
        }
    }

    /// <summary>
    /// Resolves the flow by name and binds its own <c>FlowConfiguration:&lt;FlowName&gt;</c> section. A flow
    /// without a dedicated section gets an empty one, never the sibling flows' keys.
    /// </summary>
    private IPlaywrightFlow ResolveAndConfigure(string flowName, IConfiguration flowConfiguration)
    {
        var flow = FlowDiscovery.Resolve(flowName);
        flow.Context = Context;
        flow.BaseUrl = Configuration.BaseUrl;

        var validationErrors = flow.LoadAndValidateConfiguration(flowConfiguration.GetSection(flowName));
        if (validationErrors is { Count: > 0 })
            throw new InvalidOperationException(
                $"Flow '{flowName}' configuration is invalid: " +
                string.Join("; ", validationErrors.Select(error => error.ErrorMessage)));

        return flow;
    }

    /// <summary>
    /// Sets the configured viewport on the page. Best-effort: a CDP target that rejects a device-metrics override
    /// (some remote/cluster browsers do) must not abort the whole run before any flow — log and continue.
    /// </summary>
    private async Task TrySetViewportAsync(IPage page)
    {
        try
        {
            await page.SetViewportSizeAsync(Configuration.ViewportWidth, Configuration.ViewportHeight);
        }
        catch (PlaywrightException viewportFailure)
        {
            Context.Logger.LogWarning(
                "Could not set viewport to {Width}x{Height}: {Message}. Continuing at the browser's current size.",
                Configuration.ViewportWidth, Configuration.ViewportHeight, viewportFailure.Message);
        }
    }

    /// <summary>
    /// Captures a PNG of the current page for failure diagnostics. Best-effort: if the page is gone
    /// (or the capture times out) it logs a warning and returns null rather than masking the original failure.
    /// </summary>
    private async Task<byte[]?> TryCaptureFailureScreenshot(IPage page)
    {
        try
        {
            return await page.ScreenshotAsync(new PageScreenshotOptions
            {
                // Viewport-sized by default (exactly the configured display); opt into the whole document with
                // FullPageScreenshot=true.
                FullPage = Configuration.FullPageScreenshot,
                Timeout = FailureScreenshotTimeoutMs,
            });
        }
        catch (Exception screenshotFailure)
        {
            Context.Logger.LogWarning("Could not capture a failure screenshot: {Message}", screenshotFailure.Message);
            return null;
        }
    }

    /// <summary>
    /// Speeds up headless runs by blocking heavy assets and disabling animations. Both are installed before the
    /// first navigation and (via routing / an init script) survive the subsequent navigations the flows make.
    /// </summary>
    private async Task ApplyHeadlessOptimizations(IPage page)
    {
        if (!Configuration.Headless) return;

        if (Configuration.BlockAssets)
            await page.RouteAsync(
                "**/*.{png,jpg,jpeg,gif,svg,ico,woff,woff2,ttf,eot}",
                route => route.AbortAsync());

        if (Configuration.DisableAnimations)
            await page.AddInitScriptAsync(
                """
                const style = document.createElement('style');
                style.textContent = '*, *::before, *::after { transition: none !important; animation: none !important; }';
                (document.head ?? document.documentElement).appendChild(style);
                """);
    }

    /// <summary>Reads the current session name from the runner's probe execution scope.</summary>
    private string CurrentSessionName()
    {
        var sessionName = Activity.Current?.GetBaggageItem(SessionNameBaggageKey);
        if (!string.IsNullOrWhiteSpace(sessionName)) return sessionName;

        Context.Logger.LogWarning(
            "Probe is running without a session execution scope; per-flow results will not be session-scoped.");
        return PlaywrightFlowResults.UnscopedSessionName;
    }

    /// <summary>True only when running with a real TTY — CI / redirected pipes return false.</summary>
    private static bool IsInteractiveConsole() =>
        Environment.UserInteractive && !Console.IsInputRedirected && !Console.IsOutputRedirected;

    private async Task SafeTeardownAsync(string action, Func<ValueTask> teardown)
    {
        try
        {
            await teardown();
        }
        catch (Exception teardownFailure)
        {
            Context.Logger.LogWarning("Failed to {Action} during teardown: {Message}", action, teardownFailure.Message);
        }
    }
}
