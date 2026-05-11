using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace QaaS.Probes.Playwright.Configuration;

/// <summary>
/// Probe-level configuration bound from YAML ProbeConfiguration section.
///
/// Note: FlowConfiguration is NOT listed here — it's a separate YAML subsection
/// that gets passed directly to each flow's own typed config record.
/// We set ErrorOnUnknownConfiguration=false in the probe so this doesn't crash
/// when FlowConfiguration is present in the YAML.
/// </summary>
public class PlaywrightFlowConfig
{
    /// <summary>
    /// The website URL. The probe navigates here before running any flows.
    /// All flows can access this via the BaseUrl property.
    /// Change this to switch between environments (staging, production, etc).
    /// </summary>
    [Required]
    public string BaseUrl { get; set; } = null!;

    /// <summary>
    /// When true (default), the browser runs invisibly in the background.
    /// When false, you see the browser — and these things change automatically:
    ///   - SlowMo defaults to 1000ms (so you can watch)
    ///   - BlockAssets and DisableAnimations are turned off (so the site looks normal)
    /// </summary>
    [DefaultValue(true)]
    public bool Headless { get; set; } = true;

    /// <summary>
    /// Block images, fonts, and icons in headless mode.
    /// Saves network time since nobody is looking at the browser anyway.
    /// Auto-disabled when Headless is false.
    /// </summary>
    [DefaultValue(true)]
    public bool BlockAssets { get; set; } = true;

    /// <summary>
    /// Disable CSS transitions and animations in headless mode.
    /// Prevents flaky waits on animated elements.
    /// Auto-disabled when Headless is false.
    /// </summary>
    [DefaultValue(true)]
    public bool DisableAnimations { get; set; } = true;

    /// <summary>Max time in ms to wait for any element before failing.</summary>
    [DefaultValue(30000)]
    public int DefaultTimeout { get; set; } = 30000;

    /// <summary>
    /// Delay between flows in ms. Defaults to 1000 when Headless is false.
    /// Set explicitly to override the auto behavior.
    /// </summary>
    [DefaultValue(0)]
    public int SlowMo { get; set; }

    /// <summary>
    /// Keep the browser window open after all flows finish.
    /// Only works when Headless is false — useful for inspecting the final page state.
    /// </summary>
    [DefaultValue(false)]
    public bool KeepOpen { get; set; }

    /// <summary>
    /// Flows that run once before the main Flows (e.g. login, cookie consent).
    /// They share the same browser context so cookies carry over to main flows.
    /// </summary>
    public string[]? SetupFlows { get; set; }

    /// <summary>
    /// Main flows to execute in order. Each is a C# class name implementing IPlaywrightFlow.
    /// They share the same browser page — navigation state, cookies, localStorage persist.
    /// </summary>
    public string[]? Flows { get; set; }

    /// <summary>
    /// CDP endpoint of the cluster Chromium (OpenShift). Used by default unless the
    /// env var BROWSER_MODE=local switches to local mode.
    /// Example: "http://chrome.qaas.internal:9222".
    /// </summary>
    public string? RemoteBrowserUrl { get; set; }

    /// <summary>
    /// CDP endpoint of a Chrome already running on the developer's machine.
    /// Used only when BROWSER_MODE=local. When set, the probe ATTACHES to this
    /// browser instead of launching a fresh one — your auth/cookies/fingerprint persist.
    ///
    /// Start your Chrome with:
    ///   chrome --remote-debugging-port=9222 --user-data-dir=/path/to/profile
    /// </summary>
    public string? LocalBrowserUrl { get; set; }

    /// <summary>
    /// Override the local Chrome binary path (escape hatch for non-standard installs).
    /// Used only when BROWSER_MODE=local AND LocalBrowserUrl is unset.
    /// When unset, Playwright finds Chrome in the standard OS install location.
    /// </summary>
    public string? BrowserExecutablePath { get; set; }
}
