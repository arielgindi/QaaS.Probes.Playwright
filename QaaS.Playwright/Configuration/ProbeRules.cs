using Microsoft.Extensions.Configuration;

namespace QaaS.Playwright.Configuration;

/// <summary>
/// The probe's rules beyond what each setting allows on its own: removed settings, settings that cannot work together,
/// and a FlowConfiguration that is a single value or holds sections of flows that do not run. Names compare
/// case-insensitively, as the binder does.
/// </summary>
internal static class ProbeRules
{
    private static readonly Dictionary<string, string> RemovedSettings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RemoteBrowserUrl"] = "removed: use BrowserUrl",
        ["LocalBrowserUrl"] = "removed: use BrowserUrl",
        ["DisableAnimations"] = "removed: it never took effect, so delete it",
    };

    /// <summary>Each removed setting that is still set, with what to do instead.</summary>
    public static IEnumerable<SettingProblem> Removed(IConfiguration probeConfiguration) =>
        probeConfiguration.GetChildren()
            .Where(setting => RemovedSettings.ContainsKey(setting.Key))
            .Select(setting => new SettingProblem(setting.Key, RemovedSettings[setting.Key]));

    public static IEnumerable<SettingProblem> Broken(IConfiguration probeConfiguration, PlaywrightFlowConfig config)
    {
        string[] setupFlows = config.SetupFlows ?? [], flows = config.Flows ?? [];
        if (setupFlows.Length + flows.Length == 0)
            yield return new("Flows", "nothing to run: Flows and SetupFlows are both empty");
        else if (config.ForEach is not null && flows.Length == 0)
            yield return new("Flows", "empty, so ForEach has nothing to run for each item");

        foreach (var (key, names) in new[] { ("SetupFlows", setupFlows), ("Flows", flows) })
            for (var index = 0; index < names.Length; index++)
                if (string.IsNullOrWhiteSpace(names[index])) yield return new($"{key}:{index}", "empty flow name");

        if (config.ForEach is null && config.Parallelism > 1)
            yield return new("Parallelism", "applies only with ForEach");
        if (config.ForEach is not null && !string.IsNullOrWhiteSpace(config.SaveStorageStatePath))
            yield return new("SaveStorageStatePath", "cannot be used with ForEach, where several workers run");
        if (config.ForEach is not null && !config.IsolateContext)
            yield return new("IsolateContext", "must stay true with ForEach: every worker needs a context of its own");

        var flowConfiguration = probeConfiguration.GetSection(PlaywrightFlowConfig.FlowConfigurationKey);
        if (!string.IsNullOrEmpty(flowConfiguration.Value))
            yield return new(flowConfiguration.Key, $"expected a section per flow, not '{flowConfiguration.Value}'");

        var flowNames = setupFlows.Concat(flows).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphanSections = flowConfiguration.GetChildren().Where(section => !flowNames.Contains(section.Key));
        foreach (var section in orphanSections)
            yield return new(section.Path, $"no flow named '{section.Key}' is in SetupFlows or Flows");
    }
}
