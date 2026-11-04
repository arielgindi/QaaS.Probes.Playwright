using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using QaaS.Framework.Configurations.ConfigurationBuilderExtensions;

namespace QaaS.Playwright.Recorder.Login;

/// <summary>A login flow's settings, read from a YAML file as QaaS reads a test's, with its placeholders resolved.</summary>
internal static class LoginSettings
{
    private const string FlowConfiguration = "FlowConfiguration";

    // The values the person typed on the start screen for a login flow, by flow, then by setting; a secret's is kept for
    // this run alone, as a Secret, and never written anywhere.
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, Capture.Secret>> Typed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The values typed on the start screen for <paramref name="flow"/>, which its YAML does not give: every reading of
    /// its settings takes them over the file's, for the login run, a test and a replay. Each is held as a
    /// <see cref="Capture.Secret"/>, so none reaches a file or the console, whatever its name.
    /// </summary>
    public static void TypeValues(string flow, IReadOnlyDictionary<string, string> values) =>
        Typed[flow] = values.Where(pair => pair.Value.Length > 0).ToDictionary(pair => pair.Key, pair => Capture.Secret.Of(pair.Value), StringComparer.OrdinalIgnoreCase);

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

        foreach (var (key, value) in Typed.GetValueOrDefault(flow) ?? new Dictionary<string, Capture.Secret>())
            values[$"{FlowConfiguration}:{flow}:{key}"] = value.Reveal();

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

    /// <summary>
    /// The <c>BaseUrl</c> the tests give <paramref name="flow"/> in <paramref name="path"/>: that of the probe that runs it
    /// as a setup flow (<c>ProbeConfiguration: BaseUrl:</c> beside <c>SetupFlows: [LoginFlow]</c>), else of the one whose
    /// <c>FlowConfiguration</c> has its values; its placeholders resolved as QaaS resolves them. Null when none does, or
    /// the file cannot be read.
    /// </summary>
    public static string? BaseUrlIn(string path, string flow) => ProbeIn(path, flow)?.BaseUrl;

    /// <summary>
    /// The probe the tests run <paramref name="flow"/> in, in <paramref name="path"/>, found as <see cref="BaseUrlIn"/>
    /// finds it: its <c>BaseUrl</c>, and whether it runs its setup flows on a blank page before it opens it
    /// (<c>SetupFlowsBeforeBaseUrl</c>, false unless it says). Null when none does, or the file cannot be read.
    /// </summary>
    public static (string BaseUrl, bool SetupFirst)? ProbeIn(string path, string flow)
    {
        IConfiguration file;
        try
        {
            file = Open(path);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        return (ProbeOf(file, flow, setup: true) ?? ProbeOf(file, flow, setup: false)) is { } probe
            ? (probe["BaseUrl"]!.Trim(), bool.TryParse(probe["SetupFlowsBeforeBaseUrl"], out var first) && first)
            : null;
    }

    // The node, or one under it, that runs the flow (or has its values) and has a BaseUrl: the probe's settings.
    private static IConfiguration? ProbeOf(IConfiguration node, string flow, bool setup)
    {
        if (node.GetSection("BaseUrl").Value is { Length: > 0 })
        {
            var setupFlows = node.GetSection("SetupFlows");
            var runs = setup
                ? setupFlows.Value?.Trim().Equals(flow, StringComparison.OrdinalIgnoreCase) == true
                  || setupFlows.GetChildren().Any(item => item.Value?.Trim().Equals(flow, StringComparison.OrdinalIgnoreCase) == true)
                : Find(node, flow) is not null;
            if (runs) return node;
        }

        foreach (var child in node.GetChildren())
            if (ProbeOf(child, flow, setup) is { } deeper)
                return deeper;
        return null;
    }

    /// <summary>Every <c>BaseUrl</c> a test's YAML gives a probe, its placeholders resolved, for the start page's choices.</summary>
    public static IEnumerable<string> BaseUrls(IConfiguration node)
    {
        if (node.GetSection("BaseUrl").Value is { Length: > 0 } url) yield return url.Trim();
        foreach (var child in node.GetChildren())
        foreach (var deeper in BaseUrls(child))
            yield return deeper;
    }

    private static bool HasFlowConfiguration(IConfiguration node) => node.GetChildren().Any(child =>
        child.Key.Equals(FlowConfiguration, StringComparison.OrdinalIgnoreCase) || HasFlowConfiguration(child));
}
