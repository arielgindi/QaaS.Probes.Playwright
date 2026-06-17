namespace QaaS.Playwright.Engine;

/// <summary>How the probe reaches Chrome: a local instance or a cluster/remote Chromium.</summary>
public enum BrowserMode
{
    /// <summary>A cluster/remote Chromium (the default; selected by <c>ENV=cluster</c>, <c>ENV=remote</c>, or unset).</summary>
    Cluster,

    /// <summary>A Chrome on the developer's machine (selected by <c>ENV=local</c>).</summary>
    Local,
}

/// <summary>
/// Resolves the <c>ENV</c> environment variable to a typed <see cref="BrowserMode"/>. It is deliberately strict:
/// anything other than <c>local</c>, <c>cluster</c>, <c>remote</c> (or empty/unset) throws, because a silent
/// fallthrough on a typo would route a developer's tests to the wrong endpoint. <c>remote</c> is an alias for
/// <see cref="BrowserMode.Cluster"/>.
/// </summary>
public static class BrowserModeResolver
{
    /// <summary>The environment variable that selects the browser mode.</summary>
    public const string EnvVar = "ENV";

    /// <summary>Resolves the mode from the <see cref="EnvVar"/> environment variable.</summary>
    /// <exception cref="InvalidOperationException">The variable is set to an unrecognized value.</exception>
    public static BrowserMode FromEnvironment() => Parse(Environment.GetEnvironmentVariable(EnvVar));

    /// <summary>Parses a raw <c>ENV</c> value into a <see cref="BrowserMode"/>.</summary>
    /// <exception cref="InvalidOperationException">The value is neither empty nor a recognized mode.</exception>
    public static BrowserMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return BrowserMode.Cluster;

        return value.Trim().ToLowerInvariant() switch
        {
            "local" => BrowserMode.Local,
            "cluster" or "remote" => BrowserMode.Cluster,
            _ => throw new InvalidOperationException(
                $"Unknown {EnvVar} value '{value}'. Accepted values: 'local', 'cluster', 'remote' " +
                "(or leave it unset; the default is cluster)."),
        };
    }
}
