using System.ComponentModel.DataAnnotations;

namespace QaaS.Playwright.Configuration;

/// <summary>
/// Probe-level configuration bound from the YAML <c>ProbeConfiguration</c> section.
/// <c>FlowConfiguration</c> is deliberately not a property here — it is a sibling subsection passed to each
/// flow's own typed config record.
/// </summary>
public sealed class PlaywrightFlowConfig
{
    /// <summary>The site URL. The probe navigates here before running any flows.</summary>
    [Required]
    [Url]
    public string BaseUrl { get; set; } = null!;

    /// <summary>
    /// Whether the run is unattended. When true (default) the probe blocks asset requests for speed; when false it
    /// slows every action down so a person can watch. How Chrome itself runs (headless or with a window) is decided
    /// by how that Chrome was started.
    /// </summary>
    public bool Headless { get; set; } = true;

    /// <summary>Block images/fonts in headless mode for speed. Ignored when <see cref="Headless"/> is false.</summary>
    public bool BlockAssets { get; set; } = true;

    /// <summary>Maximum time (ms) for any single Playwright wait.</summary>
    [Range(1, int.MaxValue)]
    public int DefaultTimeout { get; set; } = 30_000;

    /// <summary>
    /// Browser viewport width in pixels. The probe sets this display size on the page before running the flows, and
    /// failure screenshots are captured at exactly this size.
    /// </summary>
    [Range(1, 10_000)]
    public int ViewportWidth { get; set; } = 1920;

    /// <summary>
    /// Browser viewport height in pixels. The probe sets this display size on the page before running the flows, and
    /// failure screenshots are captured at exactly this size.
    /// </summary>
    [Range(1, 10_000)]
    public int ViewportHeight { get; set; } = 1080;

    /// <summary>
    /// Capture the full scrollable document in failure screenshots instead of just the viewport. Defaults to false,
    /// so a screenshot is exactly the configured <see cref="ViewportWidth"/>×<see cref="ViewportHeight"/> display.
    /// </summary>
    public bool FullPageScreenshot { get; set; }

    /// <summary>
    /// Delay (ms) Playwright waits between every action so a human can watch. Leave unset to use the default
    /// (2000 in visible mode, 0 headless); set it explicitly — including 0 — to override.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int? SlowMo { get; set; }

    /// <summary>
    /// Keep the page open for interactive inspection after the flows finish (visible + interactive runs only).
    /// </summary>
    public bool KeepOpen { get; set; }

    /// <summary>Flows that run once before <see cref="Flows"/> (login, cookie consent, etc.). Null means none.</summary>
    public string[]? SetupFlows { get; set; }

    /// <summary>Main flows to run in order — class names of types implementing IPlaywrightFlow. Null means none.</summary>
    public string[]? Flows { get; set; }

    /// <summary>
    /// When set, the browser context's authentication state (cookies + localStorage) is written to this path after
    /// the flows succeed. Pair it with <see cref="LoadStorageStatePath"/> in other sessions to log in once and reuse
    /// the session — including across sessions that run in parallel. The session that saves must finish before the
    /// sessions that load it start. sessionStorage is not captured (a Playwright storage-state limitation).
    /// </summary>
    public string? SaveStorageStatePath { get; set; }

    /// <summary>
    /// When set, the run starts in a fresh context seeded with the authentication state previously written to this
    /// path by a <see cref="SaveStorageStatePath"/> run, so it begins already logged in and can omit the login flow.
    /// The file must already exist when the run starts.
    /// </summary>
    public string? LoadStorageStatePath { get; set; }

    /// <summary>
    /// Run in a fresh browser context of its own, disposed afterwards, instead of the browser's shared default
    /// context. Set it when parallel sessions log in as different users, so they cannot overwrite each other's cookies.
    /// </summary>
    public bool IsolateContext { get; set; }

    /// <summary>
    /// CDP endpoint of the Chrome to run in, e.g. <c>ws://chrome.&lt;namespace&gt;.svc.cluster.local:3000?token=...</c>
    /// or <c>http://localhost:9222</c>. Defaults to browser-defaults.yaml. When it is on this machine and nothing
    /// answers there, the probe starts Chrome. To use another browser on your machine only, write
    /// <c>BrowserUrl: ${BROWSER_URL ?? ws://...}</c> and set the BROWSER_URL environment variable.
    /// </summary>
    public string? BrowserUrl { get; set; }

    /// <summary>The Chrome binary to start when <see cref="BrowserUrl"/> is on this machine; found automatically if unset.</summary>
    public string? BrowserExecutablePath { get; set; }
}
