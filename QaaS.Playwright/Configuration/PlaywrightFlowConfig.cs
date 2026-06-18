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
    /// Whether the run is unattended. When true (default) the probe blocks asset requests and disables CSS
    /// animations for speed; when false it forces local mode and applies slow-mo so a human can watch. This does
    /// not change the actual headless mode of the remote/launched Chrome — that is decided by how Chrome started.
    /// </summary>
    public bool Headless { get; set; } = true;

    /// <summary>Block images/fonts in headless mode for speed. Ignored when <see cref="Headless"/> is false.</summary>
    public bool BlockAssets { get; set; } = true;

    /// <summary>Disable CSS animations in headless mode to avoid flaky waits.</summary>
    public bool DisableAnimations { get; set; } = true;

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
    /// CDP endpoint of the cluster Chromium, used in the default (cluster) mode — for example
    /// <c>ws://chrome.&lt;namespace&gt;.svc.cluster.local:3000?token=&lt;token&gt;</c>. Required when neither
    /// <c>ENV=local</c> nor <see cref="Headless"/>=false selects local mode; falls back to browser-defaults.yaml.
    /// </summary>
    public string? RemoteBrowserUrl { get; set; }

    /// <summary>
    /// CDP endpoint of a Chrome on the developer's machine, used when <c>ENV=local</c> (or <see cref="Headless"/>
    /// is false). Defaults to <c>http://localhost:9222</c>; the probe auto-launches Chrome at this port if it is
    /// not already running.
    /// </summary>
    public string? LocalBrowserUrl { get; set; }

    /// <summary>
    /// Path to a Chrome binary, used as the launch target in local mode when Chrome is not in a standard install
    /// location. Ignored in cluster mode.
    /// </summary>
    public string? BrowserExecutablePath { get; set; }
}
