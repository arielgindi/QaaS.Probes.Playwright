using System.ComponentModel.DataAnnotations;

namespace QaaS.Probes.Playwright.Configuration;

/// <summary>
/// Probe-level configuration bound from the YAML ProbeConfiguration section.
/// FlowConfiguration is NOT a property here — it's a sibling subsection passed
/// to each flow's own typed config record.
/// </summary>
public class PlaywrightFlowConfig
{
    /// <summary>The site URL. The probe navigates here before running any flows.</summary>
    [Required]
    public string BaseUrl { get; set; } = null!;

    /// <summary>
    /// Behaviour switch for "is a human watching?". When false (default), the probe
    /// blocks asset requests and disables CSS animations for speed. Note: this does
    /// NOT control the actual headless mode of the remote/launched Chrome — that's
    /// decided by how Chrome was started (cluster image, local install).
    /// </summary>
    public bool Headless { get; set; } = true;

    /// <summary>Block images/fonts in headless mode for speed. Ignored when Headless=false.</summary>
    public bool BlockAssets { get; set; } = true;

    /// <summary>Disable CSS animations in headless mode to avoid flaky waits.</summary>
    public bool DisableAnimations { get; set; } = true;

    /// <summary>Max time (ms) for any single Playwright wait.</summary>
    public int DefaultTimeout { get; set; } = 30000;

    /// <summary>
    /// Delay (ms) between every Playwright action (click, type, fill, etc.) so a human
    /// can watch. Defaults to 2000 when Headless=false, 0 otherwise. Set explicitly to override.
    /// </summary>
    public int SlowMo { get; set; }

    /// <summary>Keep the browser open after flows finish (Headless=false only). Useful for inspecting state.</summary>
    public bool KeepOpen { get; set; }

    /// <summary>Flows that run once before the main Flows (login, cookie consent, etc).</summary>
    public string[]? SetupFlows { get; set; }

    /// <summary>Main flows to execute in order. Class names of types implementing IPlaywrightFlow.</summary>
    public string[]? Flows { get; set; }

    /// <summary>
    /// CDP endpoint of the cluster Chromium. Used in default (cluster) mode.
    /// Example: "http://chrome.qaas.internal:9222". Required when BROWSER_MODE is unset.
    /// </summary>
    public string? RemoteBrowserUrl { get; set; }

    /// <summary>
    /// CDP endpoint of a Chrome on the developer's laptop. Used when BROWSER_MODE=local.
    /// Defaults to http://localhost:9222. The probe auto-launches Chrome at this port if
    /// it isn't already running.
    /// </summary>
    public string? LocalBrowserUrl { get; set; }

    /// <summary>
    /// Path to a Chrome binary. Used as the launch target in local mode when the standard
    /// install locations don't contain Chrome. Ignored in cluster mode.
    /// </summary>
    public string? BrowserExecutablePath { get; set; }
}
