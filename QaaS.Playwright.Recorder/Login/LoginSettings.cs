using Microsoft.Extensions.Configuration;
using QaaS.Framework.Configurations.ConfigurationBuilderExtensions;

namespace QaaS.Playwright.Recorder.Login;

/// <summary>A login flow's settings, read from a YAML file as QaaS reads a test's, with its placeholders resolved.</summary>
internal static class LoginSettings
{
    private const string FlowConfiguration = "FlowConfiguration";

    /// <summary>
    /// The <c>FlowConfiguration:&lt;flow&gt;</c> section of <paramref name="path"/>, wherever it is in the file: the
    /// recorder's own <c>&lt;Flow&gt;.flow.yaml</c>, a probe's ProbeConfiguration, or a whole test.qaas.yaml, whose
    /// first such section is taken. Returned as a FlowConfiguration section, which the probe gives its flows; with no
    /// file, or a file of no FlowConfiguration (<c>{}</c>, or <c>LoginFlow: {}</c>, as for a flow of no settings), an
    /// empty one, so the flow's settings keep their defaults. <c>${NAME}</c> reads an environment variable, and
    /// <c>${NAME ?? value}</c> has a default, as in QaaS.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The file is not YAML, or has a FlowConfiguration for other flows only, as a wrong file would.
    /// </exception>
    public static IConfiguration Read(string? path, string flow)
    {
        var values = new Dictionary<string, string?>();
        if (path is not null)
        {
            var file = Open(path);
            var section = Find(file, flow);
            if (section is null && HasFlowConfiguration(file))
                throw new InvalidOperationException($"{path} has no {FlowConfiguration}: {flow}: section.");
            foreach (var (key, value) in section?.AsEnumerable(makePathsRelative: true) ?? [])
                values[$"{FlowConfiguration}:{flow}:{key}"] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection(FlowConfiguration);
    }

    /// <summary>The YAML file at <paramref name="path"/>, as QaaS reads a test's, its placeholders resolved.</summary>
    /// <exception cref="InvalidOperationException">It cannot be read, or is not YAML.</exception>
    public static IConfiguration Open(string path)
    {
        try
        {
            return new ConfigurationBuilder().AddYaml(path).AddEnvironmentVariables().AddPlaceholderResolver().Build();
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            throw new InvalidOperationException($"Could not read {path}: {failure.Message}", failure);
        }
    }

    /// <summary>The first <c>FlowConfiguration:&lt;flow&gt;</c> section of <paramref name="node"/>, or null.</summary>
    public static IConfigurationSection? Find(IConfiguration node, string flow)
    {
        foreach (var child in node.GetChildren())
        {
            if (child.Key.Equals(FlowConfiguration, StringComparison.OrdinalIgnoreCase)
                && child.GetChildren().FirstOrDefault(entry => entry.Key.Equals(flow, StringComparison.OrdinalIgnoreCase)) is { } found)
                return found;
            if (Find(child, flow) is { } deeper) return deeper;
        }

        return null;
    }

    private static bool HasFlowConfiguration(IConfiguration node) => node.GetChildren().Any(child =>
        child.Key.Equals(FlowConfiguration, StringComparison.OrdinalIgnoreCase) || HasFlowConfiguration(child));
}
