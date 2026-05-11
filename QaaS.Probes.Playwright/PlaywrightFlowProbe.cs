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
/// a local Chrome on the developer's laptop (env: BROWSER_MODE=local). Local mode
/// auto-launches Chrome if it isn't running, so subsequent runs reuse it.
/// </summary>
public class PlaywrightFlowProbe : BaseProbe<PlaywrightFlowConfig>
{
    private const string DefaultLocalBrowserUrl = "http://localhost:9222";
    private static readonly TimeSpan LocalStartupTimeout = TimeSpan.FromSeconds(60);

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
        var flowNames  = Configuration.Flows ?? [];
        if (setupNames.Length == 0 && flowNames.Length == 0)
        {
            Context.Logger.LogWarning("No flows configured");
            return;
        }

        var visible = !Configuration.Headless;
        var slowMo = Configuration.SlowMo > 0 ? Configuration.SlowMo : (visible ? 1000 : 0);

        var timer = Stopwatch.StartNew();
        using var pw = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await ConnectBrowserAsync(pw);
        await using var ctx = await browser.NewContextAsync();
        var page = await ctx.NewPageAsync();
        page.SetDefaultTimeout(Configuration.DefaultTimeout);

        await ApplyHeadlessOptimizations(page, visible);

        Context.Logger.LogInformation("Navigating to {BaseUrl}", Configuration.BaseUrl);
        await page.GotoAsync(Configuration.BaseUrl);

        var flowConfig = _rawConfiguration.GetSection("FlowConfiguration");
        await RunFlows(setupNames, flowConfig, page, slowMo, label: "Setup");
        await RunFlows(flowNames,  flowConfig, page, slowMo, label: "Running");

        Context.Logger.LogInformation("Done — {Ms}ms", timer.ElapsedMilliseconds);

        if (visible && Configuration.KeepOpen)
        {
            Context.Logger.LogInformation("Browser staying open. Close the inspector to continue.");
            await page.PauseAsync();
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
        if (BrowserModeResolver.FromEnvironment() == BrowserMode.Local)
        {
            var url = string.IsNullOrWhiteSpace(Configuration.LocalBrowserUrl)
                ? DefaultLocalBrowserUrl : Configuration.LocalBrowserUrl!;
            await LocalChromeLauncher.EnsureRunningAsync(
                url, Configuration.BrowserExecutablePath, LocalStartupTimeout, Context.Logger);
            return await AttachAsync(pw, url, "Local");
        }

        if (string.IsNullOrWhiteSpace(Configuration.RemoteBrowserUrl))
            throw new InvalidOperationException(
                "Cluster mode is active but RemoteBrowserUrl is not set in YAML. " +
                $"Set ProbeConfiguration.RemoteBrowserUrl or run with {BrowserModeResolver.EnvVar}=local.");

        return await AttachAsync(pw, Configuration.RemoteBrowserUrl, "Cluster");
    }

    private async Task<IBrowser> AttachAsync(IPlaywright pw, string url, string mode)
    {
        Context.Logger.LogInformation("{Mode} mode → {Url}", mode, url);
        try { return await pw.Chromium.ConnectOverCDPAsync(url); }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to connect to {mode.ToLowerInvariant()} Chrome at {url}. {ex.Message}", ex);
        }
    }
}
