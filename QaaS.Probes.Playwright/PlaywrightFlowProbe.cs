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

        // Reuse the browser's existing default context when attaching to a real
        // Chrome — that's where the user's cookies/sessions/extensions live.
        // NewContextAsync() would create an incognito-like context with empty state.
        // Only create a new context as a fallback (e.g., a launched Browserless
        // instance with no default context).
        var existingContext = browser.Contexts.FirstOrDefault();
        var ctx = existingContext ?? await browser.NewContextAsync();
        var ownsContext = existingContext is null;

        var page = await ctx.NewPageAsync();
        page.SetDefaultTimeout(Configuration.DefaultTimeout);

        try
        {
            await ApplyHeadlessOptimizations(page, visible);

            Context.Logger.LogInformation("Navigating to {BaseUrl}", Configuration.BaseUrl);
            await page.GotoAsync(Configuration.BaseUrl);

            var flowConfig = _rawConfiguration.GetSection("FlowConfiguration");
            await RunFlows(setupNames, flowConfig, page, slowMo, label: "Setup");
            await RunFlows(flowNames, flowConfig, page, slowMo, label: "Running");

            Context.Logger.LogInformation("Done — {Ms}ms", timer.ElapsedMilliseconds);

            if (visible && Configuration.KeepOpen)
            {
                Context.Logger.LogInformation("Browser staying open. Close the inspector to continue.");
                await page.PauseAsync();
            }
        }
        finally
        {
            // Close the tab we opened so we don't leave debris in the user's Chrome.
            if (!Configuration.KeepOpen) await page.CloseAsync();
            // Dispose the context only if we created it — never the user's default one.
            if (ownsContext) await ctx.DisposeAsync();
        }
    }

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
            await ResolveAndConfigure(name, flowConfig).RunAsync(page);
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
        return await AttachAsync(pw, clusterUrl, "Cluster");
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
            SlowMo = slowMo > 0 ? slowMo : (float?)null
        };
        try { return await pw.Chromium.ConnectOverCDPAsync(url, options); }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to connect to {mode.ToLowerInvariant()} Chrome at {url}. {ex.Message}", ex);
        }
    }
}
