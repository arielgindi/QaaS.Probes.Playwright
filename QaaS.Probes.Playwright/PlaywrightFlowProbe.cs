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
/// QaaS probe that runs recorded Playwright browser flows.
///
/// How it works:
/// 1. QaaS Runner discovers this probe by class name (PlaywrightFlowProbe)
/// 2. Runner passes ProbeConfiguration from YAML → LoadAndValidateConfiguration
/// 3. We bind our own config (BaseUrl, Headless, Flows, etc.) and save the raw IConfiguration
/// 4. At runtime, we get a Chromium browser (local launch OR remote CDP connection),
///    navigate to BaseUrl, then run each flow in order
/// 5. Each flow gets the FlowConfiguration subsection for its own typed config binding
/// 6. All flows share one browser page — cookies and session state persist across flows
///
/// Two modes (controlled by the BROWSER_MODE env var):
///   - Default          → connect to RemoteBrowserUrl (cluster Chromium in OpenShift)
///   - BROWSER_MODE=local → attach to LocalBrowserUrl if set, else launch
///                          BrowserExecutablePath, else launch the system Chrome
///
/// The raw IConfiguration is saved because FlowConfiguration is a dynamic subsection
/// whose type depends on which flow is running — we can't bind it at probe config time.
/// </summary>
public class PlaywrightFlowProbe : BaseProbe<PlaywrightFlowConfig>
{
    private const string BrowserModeEnvVar = "BROWSER_MODE";

    private IConfiguration _rawConfiguration = null!;

    /// <summary>
    /// We set ErrorOnUnknownConfiguration=false because the YAML contains FlowConfiguration
    /// which is NOT a property on PlaywrightFlowConfig — it's consumed by the flow classes.
    /// Without this, QaaS's binder would throw when it encounters FlowConfiguration.
    /// </summary>
    protected override BinderOptions GetConfigurationBinderOptions() => new()
    {
        ErrorOnUnknownConfiguration = false
    };

    public override List<ValidationResult>? LoadAndValidateConfiguration(IConfiguration configuration)
    {
        // Save the raw IConfiguration so we can extract FlowConfiguration subsection later.
        // BaseProbe.LoadAndValidateConfiguration only binds PlaywrightFlowConfig fields.
        _rawConfiguration = configuration;
        return base.LoadAndValidateConfiguration(configuration);
    }

    public override void Run(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
    {
        // Task.Run ensures we're on a threadpool thread without a SynchronizationContext.
        // This prevents deadlocks when Playwright posts async continuations.
        Task.Run(() => RunAsync()).GetAwaiter().GetResult();
    }

    private async Task RunAsync()
    {
        var setupNames = Configuration.SetupFlows ?? [];
        var flowNames = Configuration.Flows ?? [];

        if (setupNames.Length == 0 && flowNames.Length == 0)
        {
            Context.Logger.LogWarning("No flows configured");
            return;
        }

        // When the browser is visible, make it watchable:
        // - SlowMo defaults to 1s between flows so you can follow along
        // - Asset blocking and animation disabling are skipped so the site looks normal
        var visible = !Configuration.Headless;
        var slowMo = Configuration.SlowMo > 0 ? Configuration.SlowMo : visible ? 1000 : 0;

        var timer = Stopwatch.StartNew();

        using var pw = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await GetBrowserAsync(pw);
        await using var ctx = await browser.NewContextAsync();
        var page = await ctx.NewPageAsync();
        page.SetDefaultTimeout(Configuration.DefaultTimeout);

        // Headless optimizations — skip visual things nobody can see.
        // Note: when connected via CDP, the actual headless mode is controlled by how
        // Chrome was launched in the container. Headless here still gates these tweaks.
        if (!visible && Configuration.BlockAssets)
            await page.RouteAsync("**/*.{png,jpg,jpeg,gif,svg,ico,woff,woff2,ttf,eot}",
                r => r.AbortAsync());

        if (!visible && Configuration.DisableAnimations)
            await page.AddStyleTagAsync(new()
            {
                Content = "*, *::before, *::after { transition: none !important; animation: none !important; }"
            });

        // Navigate to BaseUrl once — all flows start from here
        Context.Logger.LogInformation("Navigating to {BaseUrl}", Configuration.BaseUrl);
        await page.GotoAsync(Configuration.BaseUrl);

        // FlowConfiguration is a YAML subsection inside ProbeConfiguration.
        // Each flow gets either its own named subsection (FlowConfiguration:LoginFlow:)
        // or the shared root (FlowConfiguration:) if no named section exists.
        var flowConfig = _rawConfiguration.GetSection("FlowConfiguration");

        // Setup flows run once — login, cookie consent, etc.
        // They share the same browser context so cookies carry to main flows.
        foreach (var name in setupNames)
        {
            Context.Logger.LogInformation("Setup: {Name}", name);
            if (slowMo > 0) await Task.Delay(slowMo);
            await ResolveAndConfigure(name, flowConfig).RunAsync(page);
        }

        // Main flows run in order, same page, same cookies
        foreach (var name in flowNames)
        {
            Context.Logger.LogInformation("Running: {Name}", name);
            if (slowMo > 0) await Task.Delay(slowMo);
            await ResolveAndConfigure(name, flowConfig).RunAsync(page);
        }

        Context.Logger.LogInformation("Done — {Ms}ms", timer.ElapsedMilliseconds);

        if (visible && Configuration.KeepOpen)
        {
            Context.Logger.LogInformation("Browser staying open. Close the inspector to continue.");
            await page.PauseAsync();
        }
    }

    /// <summary>
    /// Resolves the browser based on the BROWSER_MODE env var:
    ///   unset   → cluster mode, attach to RemoteBrowserUrl
    ///   "local" → local mode: attach to LocalBrowserUrl if set, else launch Chrome
    /// </summary>
    private async Task<IBrowser> GetBrowserAsync(IPlaywright pw)
    {
        var isLocal = string.Equals(
            Environment.GetEnvironmentVariable(BrowserModeEnvVar),
            "local", StringComparison.OrdinalIgnoreCase);

        // Cluster mode — always attach via CDP. RemoteBrowserUrl is required.
        if (!isLocal)
        {
            if (string.IsNullOrWhiteSpace(Configuration.RemoteBrowserUrl))
                throw new InvalidOperationException(
                    "Cluster mode is active but RemoteBrowserUrl is not set in YAML. " +
                    $"Set RemoteBrowserUrl or run with {BrowserModeEnvVar}=local.");

            Context.Logger.LogInformation("Cluster mode → {Url}", Configuration.RemoteBrowserUrl);
            return await pw.Chromium.ConnectOverCDPAsync(Configuration.RemoteBrowserUrl);
        }

        // Local mode — attach to a running Chrome if a URL is given (keeps auth/fingerprint),
        // otherwise launch a fresh one (from BrowserExecutablePath if set, else system Chrome).
        if (!string.IsNullOrWhiteSpace(Configuration.LocalBrowserUrl))
        {
            Context.Logger.LogInformation("Local mode (attach) → {Url}", Configuration.LocalBrowserUrl);
            return await pw.Chromium.ConnectOverCDPAsync(Configuration.LocalBrowserUrl);
        }

        var launch = new BrowserTypeLaunchOptions
        {
            Headless = Configuration.Headless,
            ExecutablePath = Configuration.BrowserExecutablePath,
            Channel = string.IsNullOrWhiteSpace(Configuration.BrowserExecutablePath) ? "chrome" : null,
        };
        Context.Logger.LogInformation("Local mode (launch) → {Source}",
            launch.ExecutablePath ?? "system Chrome");
        return await pw.Chromium.LaunchAsync(launch);
    }

    /// <summary>
    /// Discovers a flow class by name, sets its context and BaseUrl,
    /// and binds its configuration from the FlowConfiguration YAML section.
    ///
    /// Config resolution: tries FlowConfiguration:{name} first (per-flow config),
    /// falls back to FlowConfiguration root (shared config).
    /// This lets you put LoginFlow and CreateMissionFlow config in separate sections
    /// or share a single section if all flows use the same config shape.
    /// </summary>
    private IPlaywrightFlow ResolveAndConfigure(string name, IConfiguration flowConfig)
    {
        var flow = FlowDiscovery.Resolve(name);
        flow.Context = Context;
        flow.BaseUrl = Configuration.BaseUrl;

        var specificSection = flowConfig.GetSection(name);
        flow.LoadAndValidateConfiguration(specificSection.Exists() ? specificSection : flowConfig);

        return flow;
    }
}
