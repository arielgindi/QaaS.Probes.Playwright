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
/// QaaS probe that discovers and runs Playwright flow classes.
///
/// Flow classes implement <see cref="IPlaywrightFlow"/> and are referenced by name in YAML.
/// The probe handles browser lifecycle, performance optimizations, and passes
/// the <c>FlowConfiguration</c> YAML section to each flow for typed config binding.
/// </summary>
public class PlaywrightFlowProbe : BaseProbe<PlaywrightFlowConfig>
{
    // Saved so we can extract the FlowConfiguration subsection at runtime,
    // since BaseProbe only binds the probe-level config.
    private IConfiguration _rawConfiguration = null!;

    public override List<ValidationResult>? LoadAndValidateConfiguration(IConfiguration configuration)
    {
        _rawConfiguration = configuration;
        return base.LoadAndValidateConfiguration(configuration);
    }

    public override void Run(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
    {
        // Task.Run avoids deadlocks when called from a thread with a SynchronizationContext
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

        // When the browser is visible, show everything naturally
        var visible = !Configuration.Headless;
        var slowMo = Configuration.SlowMo > 0 ? Configuration.SlowMo : visible ? 1000 : 0;

        Context.Logger.LogInformation("Starting Playwright (headless={Headless})", Configuration.Headless);
        var timer = Stopwatch.StartNew();

        using var pw = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = Configuration.Headless });
        await using var ctx = await browser.NewContextAsync();
        var page = await ctx.NewPageAsync();
        page.SetDefaultTimeout(Configuration.DefaultTimeout);

        // Headless optimizations — skip things the user can't see anyway
        if (!visible && Configuration.BlockAssets)
            await page.RouteAsync("**/*.{png,jpg,jpeg,gif,svg,ico,woff,woff2,ttf,eot}",
                r => r.AbortAsync());

        if (!visible && Configuration.DisableAnimations)
            await page.AddStyleTagAsync(new()
            {
                Content = "*, *::before, *::after { transition: none !important; animation: none !important; }"
            });

        // The FlowConfiguration YAML section is passed to each flow for typed config binding
        var flowConfig = _rawConfiguration.GetSection("FlowConfiguration");

        // Setup flows run once — login, cookie consent, etc.
        // They share the same browser context so cookies carry to main flows.
        foreach (var name in setupNames)
        {
            Context.Logger.LogInformation("Setup: {Name}", name);
            if (slowMo > 0) await Task.Delay(slowMo);
            await ResolveAndConfigure(name, flowConfig).RunAsync(page);
        }

        // Main flows
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

    private IPlaywrightFlow ResolveAndConfigure(string name, IConfiguration flowConfig)
    {
        var flow = FlowDiscovery.Resolve(name);
        flow.Context = Context;
        flow.LoadAndValidateConfiguration(flowConfig);
        return flow;
    }
}
