using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Probe;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Playwright.Browser;
using QaaS.Playwright.Configuration;
using QaaS.Playwright.Flows;

namespace QaaS.Playwright;

/// <summary>
/// Opens a page on BaseUrl, runs SetupFlows and then Flows on it in order, and records each flow's outcome for
/// <see cref="PlaywrightFlowAssertion"/>. The first failing flow stops the run and fails the session.
/// </summary>
public sealed class PlaywrightFlowProbe : BaseProbe<PlaywrightFlowConfig>
{
    // The runner publishes the running session's and probe's names under these Activity baggage keys.
    private const string SessionNameBaggageKey = "qaas.probe.session-name";
    private const string ProbeNameBaggageKey = "qaas.probe.probe-name";

    private IConfiguration _flowConfiguration = null!;

    // FlowConfiguration sits next to the probe's own keys, so the binder must accept unknown keys; the probe warns
    // about the ones it ignores instead.
    protected override BinderOptions GetConfigurationBinderOptions() => new() { ErrorOnUnknownConfiguration = false };

    public override List<ValidationResult>? LoadAndValidateConfiguration(IConfiguration configuration)
    {
        var errors = base.LoadAndValidateConfiguration(configuration);
        _flowConfiguration = configuration.GetSection(PlaywrightFlowConfig.FlowConfigurationKey);
        foreach (var warning in UnknownSettings.Find(configuration, Configuration))
            Context.Logger.LogWarning("{Warning}", warning);
        return errors;
    }

    // IProbe.Run is synchronous; this is the one place the async run is waited on.
    public override void Run(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList) =>
        Task.Run(() => RunAsync(sessionDataList, dataSourceList)).GetAwaiter().GetResult();

    private async Task RunAsync(IImmutableList<SessionData> sessions, IImmutableList<DataSource> dataSources)
    {
        string[] setupFlows = Configuration.SetupFlows ?? [], flows = Configuration.Flows ?? [];
        if (setupFlows.Length + flows.Length == 0)
        {
            Context.Logger.LogWarning("No flows configured; nothing to run.");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var probeName = Activity.Current?.GetBaggageItem(ProbeNameBaggageKey);
        var runner = new FlowRunner(Context, CurrentSessionName(), probeName, Configuration, _flowConfiguration);
        if (Configuration.ForEach is { } dataSource)
            await new ForEachRun(runner, Configuration, Context.Logger)
                .RunAsync(FlowItem.AllOf(dataSource, dataSources, sessions), setupFlows, flows);
        else
            await RunOnceAsync(runner, setupFlows, flows);

        Context.Logger.LogInformation("Done — {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
    }

    private async Task RunOnceAsync(FlowRunner runner, string[] setupFlows, string[] flows)
    {
        await using var browser = await BrowserSession.OpenAsync(Configuration, Context.Logger);
        Context.Logger.LogInformation("Navigating to {BaseUrl}", Configuration.BaseUrl);
        await browser.Page.GotoAsync(Configuration.BaseUrl);

        await runner.RunAsync(setupFlows, browser.Page);
        await runner.RunAsync(flows, browser.Page);

        // Saved only after every flow passed, so a failed login is never reused.
        if (!string.IsNullOrWhiteSpace(Configuration.SaveStorageStatePath))
            await browser.SaveStorageStateAsync(Configuration.SaveStorageStatePath);

        await PauseForInspectionAsync(browser);
    }

    // KeepOpen pauses on the Playwright inspector, which would wait forever without a person at a terminal.
    private async Task PauseForInspectionAsync(BrowserSession browser)
    {
        if (!Configuration.KeepOpen) return;
        if (Configuration.Headless || !IsInteractiveTerminal())
        {
            Context.Logger.LogWarning("KeepOpen ignored: it needs Headless: false and an interactive terminal.");
            return;
        }

        Context.Logger.LogInformation("Browser staying open. Close the inspector to continue.");
        browser.KeepPageOpen = true;
        await browser.Page.PauseAsync();
    }

    private string CurrentSessionName()
    {
        var sessionName = Activity.Current?.GetBaggageItem(SessionNameBaggageKey);
        if (!string.IsNullOrWhiteSpace(sessionName)) return sessionName;

        Context.Logger.LogWarning("Probe is running outside a session; its flow results are recorded as unscoped.");
        return PlaywrightFlowResults.UnscopedSessionName;
    }

    private static bool IsInteractiveTerminal() =>
        Environment.UserInteractive && !Console.IsInputRedirected && !Console.IsOutputRedirected;
}
