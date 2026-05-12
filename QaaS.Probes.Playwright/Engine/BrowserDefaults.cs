namespace QaaS.Probes.Playwright.Engine;

/// <summary>
/// Single source of truth for browser-related defaults shared by the probe
/// and the recorder. Edit values here once; both projects pick them up.
/// </summary>
public static class BrowserDefaults
{
    // ── URLs ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// CDP endpoint of the cluster Chromium (OpenShift). Override per-project
    /// via PlaywrightFlowConfig.RemoteBrowserUrl when needed.
    /// </summary>
    public const string RemoteUrl =
        "ws://chrome.<your-namespace>.svc.cluster.local:3000?token=internal";

    /// <summary>CDP endpoint of a Chrome on the developer's laptop.</summary>
    public const string LocalUrl = "http://localhost:9222";

    // ── Recorder ────────────────────────────────────────────────────────────

    /// <summary>Playwright channel name used by the recorder's codegen.</summary>
    public const string ChromeChannel = "chrome";

    /// <summary>Viewport size the recorder opens Chrome with.</summary>
    public const string RecorderViewport = "1920,1080";

    // ── Paths under ~/.qaas ─────────────────────────────────────────────────

    /// <summary>Root directory for QaaS-managed local state.</summary>
    public static string QaasDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".qaas");

    /// <summary>Chrome user-data dir used by the probe's auto-launch.</summary>
    public static string ChromeProfileDir => Path.Combine(QaasDir, "chrome-profile");

    /// <summary>Shared cookies + localStorage state file used by the recorder.</summary>
    public static string AuthStatePath => Path.Combine(QaasDir, "auth.json");

    // ── Timing ──────────────────────────────────────────────────────────────

    /// <summary>Time the probe waits for an auto-launched Chrome to expose CDP.</summary>
    public static readonly TimeSpan LocalStartupTimeout = TimeSpan.FromSeconds(60);
}
