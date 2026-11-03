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
    /// file, an empty one, so the flow's settings keep their defaults. <c>${NAME}</c> reads an environment variable.
    /// </summary>
    /// <exception cref="InvalidOperationException">The file has no such section, or is not YAML.</exception>
    public static IConfiguration Read(string? path, string flow)
    {
        var values = new Dictionary<string, string?>();
        if (path is not null)
        {
            IConfiguration file;
            try
            {
                file = new ConfigurationBuilder().AddYaml(path).AddEnvironmentVariables().AddPlaceholderResolver().Build();
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                throw new InvalidOperationException($"Could not read {path}: {failure.Message}", failure);
            }

            var section = Find(file, flow)
                          ?? throw new InvalidOperationException($"{path} has no {FlowConfiguration}: {flow}: section.");
            foreach (var (key, value) in section.AsEnumerable(makePathsRelative: true))
                values[$"{FlowConfiguration}:{flow}:{key}"] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection(FlowConfiguration);
    }

    private static IConfigurationSection? Find(IConfiguration node, string flow)
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
}
