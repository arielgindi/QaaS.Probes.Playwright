using Microsoft.Extensions.Configuration;

namespace QaaS.Playwright.Configuration;

/// <summary>
/// Finds settings the probe ignores, which the binder cannot report because FlowConfiguration sits next to the
/// probe's own keys. Names compare case-insensitively, as the binder does.
/// </summary>
internal static class UnknownSettings
{
    public const string FlowConfigurationKey = "FlowConfiguration";

    private static readonly HashSet<string> KnownKeys = typeof(PlaywrightFlowConfig).GetProperties()
        .Select(property => property.Name)
        .Append(FlowConfigurationKey)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>One warning per unknown ProbeConfiguration key and per FlowConfiguration section of a flow that does not run.</summary>
    public static IEnumerable<string> Find(IConfiguration probeConfiguration, PlaywrightFlowConfig config)
    {
        var unknownKeys = probeConfiguration.GetChildren()
            .Select(setting => setting.Key)
            .Where(key => !KnownKeys.Contains(key));
        foreach (var key in unknownKeys)
            yield return $"Unknown ProbeConfiguration key '{key}' is ignored. Known keys: {string.Join(", ", KnownKeys)}.";

        var flows = (config.SetupFlows ?? []).Concat(config.Flows ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphanSections = probeConfiguration.GetSection(FlowConfigurationKey).GetChildren()
            .Select(section => section.Key)
            .Where(flowName => !flows.Contains(flowName));
        foreach (var flowName in orphanSections)
            yield return $"FlowConfiguration:{flowName} is ignored: no flow named '{flowName}' is in SetupFlows or Flows.";
    }
}
