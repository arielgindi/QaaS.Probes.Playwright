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
using QaaS.Playwright.Reporting;

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
    private List<string> _configurationProblems = [];

    /// <summary>
    /// Binds the settings and checks them, and the settings of every flow they name, all at once. Every problem is
    /// returned, and thrown when the probe runs: QaaS 4.8 checks a probe's settings before it loads the probe, so it
    /// never sees them.
    /// </summary>
    public override List<ValidationResult>? LoadAndValidateConfiguration(IConfiguration configuration)
    {
        var flowConfigurationKey = PlaywrightFlowConfig.FlowConfigurationKey;
        (Configuration, var bindProblems) = StrictBinder.Bind<PlaywrightFlowConfig>(configuration, "", flowConfigurationKey);
        _flowConfiguration = configuration.GetSection(flowConfigurationKey);

        // The first problem found at a path is the most precise: "removed: use BrowserUrl" beats "not a setting".
        IEnumerable<SettingProblem> problems =
            [.. ProbeRules.Removed(configuration), .. bindProblems, .. ProbeRules.Broken(configuration, Configuration)];
        _configurationProblems =
            [.. problems.DistinctBy(problem => problem.Path).Select(problem => problem.ToString()), .. FlowProblems()];
        return [.. _configurationProblems.Select(problem => new ValidationResult(problem))];
    }

    // IProbe.Run is synchronous; this is the one place the async run is waited on.
    public override void Run(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
    {
        // Before the browser opens, and as a session failure, which the assertion reports.
        if (_configurationProblems.Count > 0)
            throw new InvalidOperationException($"ProbeConfiguration has {_configurationProblems.Count} problem(s):\n" +
                string.Join('\n', _configurationProblems.Select(problem => $"  - {problem}")));

        Task.Run(() => RunAsync(sessionDataList, dataSourceList)).GetAwaiter().GetResult();
    }

    private async Task RunAsync(IImmutableList<SessionData> sessions, IImmutableList<DataSource> dataSources)
    {
        var sessionName = BaggageItem(SessionNameBaggageKey);
        var logger = new WarningRecorder(Context, sessionName);
        if (sessionName is null)
            logger.LogWarning("Probe is running outside a session; its flow results are recorded as unscoped.");

        string[] setupFlows = Configuration.SetupFlows ?? [], flows = Configuration.Flows ?? [];
        var stopwatch = Stopwatch.StartNew();
        var runner = new FlowRunner(
            Context, sessionName, BaggageItem(ProbeNameBaggageKey), Configuration, _flowConfiguration);
        if (Configuration.ForEach is { } dataSource)
        {
            if (Configuration.KeepOpen) logger.LogWarning("KeepOpen ignored: several ForEach workers run at once.");
            await new ForEachRun(runner, Configuration, logger)
                .RunAsync(FlowItem.AllOf(dataSource, dataSources, sessions, logger), setupFlows, flows);
        }
        else
        {
            await RunOnceAsync(runner, logger, setupFlows, flows);
        }

        logger.LogInformation("Done — {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
    }

    private async Task RunOnceAsync(FlowRunner runner, ILogger logger, string[] setupFlows, string[] flows)
    {
        await using var browser = await BrowserSession.OpenAsync(Configuration, logger);
        logger.LogInformation("Navigating to {BaseUrl}", Configuration.BaseUrl);
        await browser.OpenBaseUrlAsync(Configuration.BaseUrl);

        await runner.RunAsync(setupFlows, browser);
        await runner.RunAsync(flows, browser);

        // Saved only after every flow passed, so a failed login is never reused.
        if (!string.IsNullOrWhiteSpace(Configuration.SaveStorageStatePath))
            await browser.SaveStorageStateAsync(Configuration.SaveStorageStatePath);

        await PauseForInspectionAsync(browser, logger);
    }

    // KeepOpen pauses on the Playwright inspector, which would wait forever without a person at a terminal.
    private async Task PauseForInspectionAsync(BrowserSession browser, ILogger logger)
    {
        if (!Configuration.KeepOpen) return;
        if (Configuration.Headless || !IsInteractiveTerminal())
        {
            logger.LogWarning("KeepOpen ignored: it needs Headless: false and an interactive terminal.");
            return;
        }

        logger.LogInformation("Browser staying open. Close the inspector to continue.");
        browser.KeepPageOpen = true;
        await browser.Page.PauseAsync();
    }

    // Each flow is created and its settings bound as a run would, so a missing flow or a mistake in its settings is
    // found before the browser opens.
    private List<string> FlowProblems()
    {
        var problems = new List<string>();
        var flowNames = (Configuration.SetupFlows ?? []).Concat(Configuration.Flows ?? [])
            .Where(flowName => !string.IsNullOrWhiteSpace(flowName))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var flowName in flowNames)
        {
            try
            {
                var flow = FlowDiscovery.Resolve(flowName);
                flow.Context = Context;
                var errors = flow.LoadAndValidateConfiguration(_flowConfiguration.GetSection(flowName)) ?? [];
                problems.AddRange(errors.Select(error => error.ErrorMessage ?? $"{flowName}: invalid settings"));
            }
            catch (InvalidOperationException unusable)
            {
                problems.Add(unusable.Message);
            }
        }

        return problems;
    }

    // Null when the runner did not set it, e.g. outside a session.
    private static string? BaggageItem(string key) =>
        Activity.Current?.GetBaggageItem(key) is { } value && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static bool IsInteractiveTerminal() =>
        Environment.UserInteractive && !Console.IsInputRedirected && !Console.IsOutputRedirected;
}
