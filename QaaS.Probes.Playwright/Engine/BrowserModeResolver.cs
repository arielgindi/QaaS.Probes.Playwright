namespace QaaS.Probes.Playwright.Engine;

public enum BrowserMode { Cluster, Local }

/// <summary>
/// Resolves the BROWSER_MODE env var to a typed mode. Strict: anything other than
/// "local", "cluster", "remote" (or empty/unset) throws — silent fallthrough on a typo
/// would route a developer's tests to the wrong endpoint.
/// </summary>
public static class BrowserModeResolver
{
    public const string EnvVar = "BROWSER_MODE";

    public static BrowserMode FromEnvironment() => Parse(Environment.GetEnvironmentVariable(EnvVar));

    public static BrowserMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return BrowserMode.Cluster;
        return value.Trim().ToLowerInvariant() switch
        {
            "local"                => BrowserMode.Local,
            "cluster" or "remote"  => BrowserMode.Cluster,
            _ => throw new InvalidOperationException(
                $"Unknown {EnvVar} value '{value}'. Use 'local' or leave unset (default = cluster).")
        };
    }
}
