using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Probe;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Probes.Playwright.Configuration;
using QaaS.Probes.Playwright.Engine;

namespace QaaS.Probes.Playwright;

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
public class PlaywrightFlowProbe : BaseProbe<PlaywrightFlowConfig>
{
    /// <summary>Slow-mo applied between actions in visible mode when none is configured, so a human can watch.</summary>
    private const int DefaultVisibleSlowMoMs = 2000;

    /// <summary>Upper bound on the best-effort failure screenshot, so a hung page cannot stall the run.</summary>
    private const int FailureScreenshotTimeoutMs = 5_000;

    private const int CdpConnectMaxAttempts = 3;

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

    // IProbe.Run is synchronous, so the async browser work is bridged here. This is the one sanctioned
    // sync-over-async point, dictated by the hook contract.
    public override void Run(IImmutableList<SessionData> _, IImmutableList<DataSource> __) =>
        Task.Run(RunAsync).GetAwaiter().GetResult();

    private async Task RunAsync()
    {
        var setupFlowNames = Configuration.SetupFlows ?? [];
        var flowNames = Configuration.Flows ?? [];
        if (setupFlowNames.Length == 0 && flowNames.Length == 0)
        {
            Context.Logger.LogWarning("No flows configured; nothing to run.");
            return;
        }

        var sessionName = CurrentSessionName();
        var runHeaded = !Configuration.Headless;
        var inspectAfterRun = runHeaded && Configuration.KeepOpen && IsInteractiveConsole();

        var timer = Stopwatch.StartNew();
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await ConnectBrowserAsync(playwright);

        // Reuse the existing default context (the user's cookies/sessions/extensions live there); only create —
        // and therefore own/dispose — a context when the browser has none.
        var existingContext = browser.Contexts.FirstOrDefault();
        var browserContext = existingContext ?? await browser.NewContextAsync();
        var ownsContext = existingContext is null;

        IPage? page = null;
        try
        {
            page = await browserContext.NewPageAsync();
            page.SetDefaultTimeout(Configuration.DefaultTimeout);
            await ApplyHeadlessOptimizations(page);

            Context.Logger.LogInformation("Navigating to {BaseUrl}", Configuration.BaseUrl);
            await page.GotoAsync(Configuration.BaseUrl);

            var flowConfiguration = _rawConfiguration.GetSection(FlowConfigurationSection);
            await RunFlows(setupFlowNames, flowConfiguration, page, sessionName, label: "Setup");
            await RunFlows(flowNames, flowConfiguration, page, sessionName, label: "Running");

            Context.Logger.LogInformation("Done — {ElapsedMs}ms", timer.ElapsedMilliseconds);

            if (inspectAfterRun)
            {
                Context.Logger.LogInformation("Browser staying open. Close the inspector to continue.");
                await page.PauseAsync();
            }
            else if (Configuration.KeepOpen)
            {
                Context.Logger.LogWarning(
                    "KeepOpen=true ignored — running headless or non-interactively (e.g. CI), where PauseAsync " +
                    "would hang forever.");
            }
        }
        finally
        {
            // Keep the page only when it was actually held open for interactive inspection; otherwise always
            // close it so reused Chrome instances do not accumulate orphaned tabs. Each cleanup step is guarded
            // so a teardown error never masks the real flow failure or skips the remaining teardown.
            if (page is not null && !inspectAfterRun)
                await SafeDisposeAsync("close page", () => new ValueTask(page.CloseAsync()));
            if (ownsContext)
                await SafeDisposeAsync("dispose browser context", browserContext.DisposeAsync);
        }
    }

    /// <summary>Runs each named flow in order, recording its outcome; re-throws on the first failure.</summary>
    private async Task RunFlows(string[] flowNames, IConfiguration flowConfiguration, IPage page, string sessionName, string label)
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
                var screenshot = await TryCaptureFailureScreenshotAsync(page);
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
    /// Captures a full-page PNG of the current page for failure diagnostics. Best-effort: if the page is gone
    /// (or the capture times out) it logs a warning and returns null rather than masking the original failure.
    /// </summary>
    private async Task<byte[]?> TryCaptureFailureScreenshotAsync(IPage page)
    {
        try
        {
            return await page.ScreenshotAsync(new PageScreenshotOptions
            {
                FullPage = true,
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
    /// first navigation and (via routing / an init script) survive subsequent navigations the flows make.
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

    private async Task<IBrowser> ConnectBrowserAsync(IPlaywright playwright)
    {
        // Visible mode (Headless=false) only makes sense locally — cluster Chrome runs headless in a container
        // and cannot show a window — so it forces local mode.
        var isLocal = !Configuration.Headless || BrowserModeResolver.FromEnvironment() == BrowserMode.Local;

        if (isLocal)
        {
            var localUrl = string.IsNullOrWhiteSpace(Configuration.LocalBrowserUrl)
                ? BrowserDefaults.LocalUrl
                : Configuration.LocalBrowserUrl;
            await LocalChromeLauncher.EnsureRunningAsync(
                localUrl, Configuration.BrowserExecutablePath, BrowserDefaults.LocalStartupTimeout, Context.Logger);
            return await AttachAsync(playwright, localUrl, "Local");
        }

        var clusterUrl = string.IsNullOrWhiteSpace(Configuration.RemoteBrowserUrl)
            ? BrowserDefaults.RemoteUrl
            : Configuration.RemoteBrowserUrl;
        EnsureNoTemplatePlaceholder(clusterUrl);
        return await AttachAsync(playwright, clusterUrl, "Cluster");
    }

    /// <summary>
    /// Fails fast on the common forget-to-edit case where the configured remote URL still contains a
    /// <c>&lt;your-namespace&gt;</c>-style placeholder, which would otherwise surface as a DNS/connection error
    /// only after the connect timeout.
    /// </summary>
    private static void EnsureNoTemplatePlaceholder(string url)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(url, "<[^>]+>"))
            throw new InvalidOperationException(
                $"Browser URL contains an unresolved placeholder: '{url}'. Edit browser-defaults.yaml and replace " +
                "the <...> tokens with your real values, or set ProbeConfiguration.RemoteBrowserUrl in YAML.");
    }

    /// <summary>
    /// Slow-mo (ms) Playwright waits between every action. An explicit configured value wins (including 0 to
    /// disable it); otherwise it defaults to <see cref="DefaultVisibleSlowMoMs"/> in visible mode and 0 headless.
    /// </summary>
    private int EffectiveSlowMo() =>
        Configuration.SlowMo ?? (Configuration.Headless ? 0 : DefaultVisibleSlowMoMs);

    private async Task<IBrowser> AttachAsync(IPlaywright playwright, string url, string mode)
    {
        Context.Logger.LogInformation("{Mode} mode → {Url}", mode, RedactToken(url));

        var slowMo = EffectiveSlowMo();
        var options = new BrowserTypeConnectOverCDPOptions { SlowMo = slowMo > 0 ? slowMo : null };

        // Retry with linear backoff — a Browserless rolling restart or a brief network blip would otherwise fail
        // the run on a transient.
        Exception? lastFailure = null;
        for (var attempt = 1; attempt <= CdpConnectMaxAttempts; attempt++)
        {
            try
            {
                return await playwright.Chromium.ConnectOverCDPAsync(url, options);
            }
            catch (Exception connectFailure)
            {
                lastFailure = connectFailure;
                if (attempt == CdpConnectMaxAttempts) break;
                Context.Logger.LogWarning("CDP connect attempt {Attempt}/{Max} failed: {Message}",
                    attempt, CdpConnectMaxAttempts, connectFailure.Message);
                await Task.Delay(500 * attempt);
            }
        }

        throw new InvalidOperationException(
            $"Failed to connect to {mode.ToLowerInvariant()} Chrome at {RedactToken(url)} after " +
            $"{CdpConnectMaxAttempts} attempts. {lastFailure?.Message}", lastFailure);
    }

    /// <summary>Reads the current session name from the runner's probe execution scope.</summary>
    private string CurrentSessionName()
    {
        var sessionName = Activity.Current?.GetBaggageItem(SessionNameBaggageKey);
        if (!string.IsNullOrWhiteSpace(sessionName)) return sessionName;

        Context.Logger.LogWarning(
            "Probe is running without a session execution scope; per-flow results will not be session-scoped.");
        return "(unscoped)";
    }

    /// <summary>True only when running with a real TTY — CI / redirected pipes return false.</summary>
    private static bool IsInteractiveConsole() =>
        Environment.UserInteractive && !Console.IsInputRedirected && !Console.IsOutputRedirected;

    /// <summary>Redacts any query string (which may carry an auth token) before a URL is logged.</summary>
    private static string RedactToken(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Query)
            ? uri.GetLeftPart(UriPartial.Path) + "?<redacted>"
            : url;

    private async Task SafeDisposeAsync(string what, Func<ValueTask> disposeAsync)
    {
        try
        {
            await disposeAsync();
        }
        catch (Exception teardownFailure)
        {
            Context.Logger.LogWarning("Failed to {What} during teardown: {Message}", what, teardownFailure.Message);
        }
    }
}
