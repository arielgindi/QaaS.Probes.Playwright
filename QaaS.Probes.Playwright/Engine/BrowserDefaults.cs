namespace QaaS.Probes.Playwright.Engine;

/// <summary>
/// Single source of truth for browser-related defaults shared by the probe
/// and the recorder. Edit values here once; both projects pick them up.
/// </summary>
public static class BrowserDefaults
{
    /// <summary>
    /// CDP endpoint of the cluster Chromium (OpenShift). Override per-project
    /// via PlaywrightFlowConfig.RemoteBrowserUrl when needed.
    /// </summary>
    public const string RemoteUrl =
        "ws://chrome.<your-namespace>.svc.cluster.local:3000?token=internal";

    /// <summary>
    /// CDP endpoint of a Chrome running on the developer's laptop.
    /// The probe auto-launches Chrome at this port if it isn't running.
    /// </summary>
    public const string LocalUrl = "http://localhost:9222";

    /// <summary>Playwright channel name used by the recorder's codegen.</summary>
    public const string ChromeChannel = "chrome";

    /// <summary>Time the probe waits for an auto-launched Chrome to expose CDP.</summary>
    public static readonly TimeSpan LocalStartupTimeout = TimeSpan.FromSeconds(60);
}
