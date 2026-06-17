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
/// Runs Playwright browser flows against either a cluster Chromium (default) or
/// a local Chrome on the developer's laptop (env: ENV=local). Local mode auto-launches
/// Chrome if it isn't running, so subsequent runs reuse it.
/// </summary>
public class PlaywrightFlowProbe : BaseProbe<PlaywrightFlowConfig>
{
    // All shared browser defaults live in BrowserDefaults — single source of truth.

    private IConfiguration _rawConfiguration = null!;

    protected override BinderOptions GetConfigurationBinderOptions() =>
        new() { ErrorOnUnknownConfiguration = false };

    public override List<ValidationResult>? LoadAndValidateConfiguration(IConfiguration configuration)
    {
        _rawConfiguration = configuration;
        return base.LoadAndValidateConfiguration(configuration);
    }

    public override void Run(IImmutableList<SessionData> _, IImmutableList<DataSource> __) =>
        Task.Run(RunAsync).GetAwaiter().GetResult();

    private async Task RunAsync()
    {
        var setupNames = Configuration.SetupFlows ?? [];
        var flowNames = Configuration.Flows ?? [];
        if (setupNames.Length == 0 && flowNames.Length == 0)
        {
            Context.Logger.LogWarning("No flows configured");
            return;
        }

        var visible = !Configuration.Headless;
        var slowMo = EffectiveSlowMo();

        var timer = Stopwatch.StartNew();
        using var pw = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await ConnectBrowserAsync(pw);

        // Reuse the existing default context (user's cookies/sessions/extensions live here).
        // NewContextAsync() would create an empty incognito-like context.
        var existingContext = browser.Contexts.FirstOrDefault();
        var ctx = existingContext ?? await browser.NewContextAsync();
        var ownsContext = existingContext is null;

        IPage? page = null;
        try
        {
            page = await ctx.NewPageAsync();
            page.SetDefaultTimeout(Configuration.DefaultTimeout);

            await ApplyHeadlessOptimizations(page, visible);

            Context.Logger.LogInformation("Navigating to {BaseUrl}", Configuration.BaseUrl);
            await page.GotoAsync(Configuration.BaseUrl);

            var flowConfig = _rawConfiguration.GetSection("FlowConfiguration");
            await RunFlows(setupNames, flowConfig, page, slowMo, label: "Setup");
            await RunFlows(flowNames, flowConfig, page, slowMo, label: "Running");

            Context.Logger.LogInformation("Done — {Ms}ms", timer.ElapsedMilliseconds);

            if (visible && Configuration.KeepOpen && IsInteractiveConsole())
            {
                Context.Logger.LogInformation("Browser staying open. Close the inspector to continue.");
                await page.PauseAsync();
            }
            else if (Configuration.KeepOpen)
            {
                Context.Logger.LogWarning(
                    "KeepOpen=true ignored — running headless or non-interactively (e.g. CI). " +
                    "PauseAsync would hang forever.");
            }
        }
        finally
        {
            if (page is not null && !Configuration.KeepOpen) await page.CloseAsync();
            if (ownsContext) await ctx.DisposeAsync();
        }
    }

    /// <summary>True only when running with a real TTY — CI/redirected pipes return false.</summary>
    private static bool IsInteractiveConsole() =>
        Environment.UserInteractive && !Console.IsInputRedirected && !Console.IsOutputRedirected;

    private async Task ApplyHeadlessOptimizations(IPage page, bool visible)
    {
        if (visible) return;
        if (Configuration.BlockAssets)
            await page.RouteAsync("**/*.{png,jpg,jpeg,gif,svg,ico,woff,woff2,ttf,eot}", r => r.AbortAsync());
        if (Configuration.DisableAnimations)
            await page.AddStyleTagAsync(new()
            {
                Content = "*, *::before, *::after { transition: none !important; animation: none !important; }"
            });
    }

    private async Task RunFlows(string[] names, IConfiguration flowConfig, IPage page, int slowMo, string label)
    {
        foreach (var name in names)
        {
            Context.Logger.LogInformation("{Label}: {Name}", label, name);
            if (slowMo > 0) await Task.Delay(slowMo);
            try
            {
                await ResolveAndConfigure(name, flowConfig).RunAsync(page);
                PlaywrightFlowResults.Record(Context, new PlaywrightFlowOutcome(name, Passed: true, FailureMessage: null));
            }
            catch (Exception ex)
            {
                // Record which flow failed (and why), plus a screenshot of the page at the moment of
                // failure, before unwinding — so the assertion can report a per-flow breakdown with
                // visual evidence. Re-throw so the journey stops and the runner registers a session failure.
                var screenshot = await TryCaptureFailureScreenshotAsync(page);
                PlaywrightFlowResults.Record(Context, new PlaywrightFlowOutcome(name, Passed: false, ex.Message, screenshot));
                throw;
            }
        }
    }

    /// <summary>
    /// Captures a full-page PNG of the current page for failure diagnostics. Best-effort: if the page is
    /// already gone (or the screenshot fails for any reason) it logs a warning and returns null rather than
    /// masking the original flow failure.
    /// </summary>
    private async Task<byte[]?> TryCaptureFailureScreenshotAsync(IPage page)
    {
        try
        {
            return await page.ScreenshotAsync(new PageScreenshotOptions { FullPage = true });
        }
        catch (Exception ex)
        {
            Context.Logger.LogWarning("Could not capture a failure screenshot: {Message}", ex.Message);
            return null;
        }
    }

    private IPlaywrightFlow ResolveAndConfigure(string name, IConfiguration flowConfig)
    {
        var flow = FlowDiscovery.Resolve(name);
        flow.Context = Context;
        flow.BaseUrl = Configuration.BaseUrl;
        var section = flowConfig.GetSection(name);
        flow.LoadAndValidateConfiguration(section.Exists() ? section : flowConfig);
        return flow;
    }

    private async Task<IBrowser> ConnectBrowserAsync(IPlaywright pw)
    {
        // Visible mode (Headless=false) only makes sense locally — cluster Chrome
        // runs in a headless container, can't show a window. Force local mode.
        var isLocal = !Configuration.Headless
                   || BrowserModeResolver.FromEnvironment() == BrowserMode.Local;

        if (isLocal)
        {
            var url = string.IsNullOrWhiteSpace(Configuration.LocalBrowserUrl)
                ? BrowserDefaults.LocalUrl : Configuration.LocalBrowserUrl!;
            await LocalChromeLauncher.EnsureRunningAsync(
                url, Configuration.BrowserExecutablePath,
                BrowserDefaults.LocalStartupTimeout, Context.Logger);
            return await AttachAsync(pw, url, "Local");
        }

        var clusterUrl = string.IsNullOrWhiteSpace(Configuration.RemoteBrowserUrl)
            ? BrowserDefaults.RemoteUrl : Configuration.RemoteBrowserUrl;
        EnsureNoTemplatePlaceholder(clusterUrl);
        return await AttachAsync(pw, clusterUrl, "Cluster");
    }

    /// <summary>
    /// Catches the common forget-to-edit case where the loaded RemoteBrowserUrl still
    /// contains a "&lt;your-namespace&gt;"-style placeholder. Without this check, the
    /// failure surfaces as a generic DNS / connection error 60s later.
    /// </summary>
    private static void EnsureNoTemplatePlaceholder(string url)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(url, "<[^>]+>"))
            throw new InvalidOperationException(
                $"Browser URL contains an unresolved placeholder: '{url}'. " +
                "Edit browser-defaults.yaml and replace the <...> tokens with your real values, " +
                "or set ProbeConfiguration.RemoteBrowserUrl in YAML.");
    }

    /// <summary>
    /// Resolved SlowMo (ms) between every Playwright action. Explicit YAML value wins;
    /// otherwise defaults to 2000ms when Headless=false (so a human can watch), 0 when headless.
    /// </summary>
    private int EffectiveSlowMo() =>
        Configuration.SlowMo > 0 ? Configuration.SlowMo : (!Configuration.Headless ? 2000 : 0);

    private async Task<IBrowser> AttachAsync(IPlaywright pw, string url, string mode)
    {
        Context.Logger.LogInformation("{Mode} mode → {Url}", mode, url);
        var slowMo = EffectiveSlowMo();
        var options = new BrowserTypeConnectOverCDPOptions
        {
            SlowMo = slowMo > 0 ? slowMo : null
        };

        // Retry with backoff — Browserless rolling restarts and brief network blips
        // would otherwise fail the run on a transient. Total cap ~3s.
        const int maxAttempts = 3;
        Exception? lastEx = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try { return await pw.Chromium.ConnectOverCDPAsync(url, options); }
            catch (Exception ex)
            {
                lastEx = ex;
                if (attempt == maxAttempts) break;
                Context.Logger.LogWarning("CDP connect attempt {N}/{Max} failed: {Msg}",
                    attempt, maxAttempts, ex.Message);
                await Task.Delay(500 * attempt);
            }
        }

        throw new InvalidOperationException(
            $"Failed to connect to {mode.ToLowerInvariant()} Chrome at {url} after {maxAttempts} attempts. " +
            $"{lastEx?.Message}", lastEx);
    }
}
