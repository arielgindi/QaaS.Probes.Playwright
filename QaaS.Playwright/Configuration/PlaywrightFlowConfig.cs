using System.ComponentModel.DataAnnotations;

namespace QaaS.Playwright.Configuration;

/// <summary>
/// The probe's <c>ProbeConfiguration</c>. Its sibling <c>FlowConfiguration</c> section is not a setting here: each
/// flow binds its own <c>FlowConfiguration:&lt;FlowName&gt;</c> part.
/// </summary>
public sealed class PlaywrightFlowConfig
{
    internal const string FlowConfigurationKey = "FlowConfiguration";

    /// <summary>The site to test. The probe opens it before the first flow.</summary>
    [Required]
    [Url]
    public string BaseUrl { get; set; } = null!;

    /// <summary>Flows that run first, e.g. a login. Class names of IPlaywrightFlow implementations.</summary>
    public string[]? SetupFlows { get; set; }

    /// <summary>Flows that run after SetupFlows, in order, on the same page.</summary>
    public string[]? Flows { get; set; }

    /// <summary>
    /// The name of a DataSource passed to the probe (DataSourceNames): Flows then run once per item it generates, and
    /// read the item as Item. SetupFlows run once per worker, in its own browser context.
    /// </summary>
    public string? ForEach { get; set; }

    /// <summary>With ForEach, how many workers go through the items at the same time.</summary>
    [Range(1, int.MaxValue)]
    public int Parallelism { get; set; } = 1;

    /// <summary>
    /// Whether nobody is watching. True blocks images and fonts for speed; false slows each action down (see SlowMo)
    /// so a person can follow. Whether Chrome shows a window depends only on how Chrome was started.
    /// </summary>
    public bool Headless { get; set; } = true;

    /// <summary>Block images and fonts while Headless, for speed.</summary>
    public bool BlockAssets { get; set; } = true;

    /// <summary>The longest any Playwright action waits, in milliseconds.</summary>
    [Range(1, int.MaxValue)]
    public int DefaultTimeout { get; set; } = 30_000;

    /// <summary>The page's width in pixels, and so the failure screenshot's.</summary>
    [Range(1, 10_000)]
    public int ViewportWidth { get; set; } = 1920;

    /// <summary>The page's height in pixels, and so the failure screenshot's.</summary>
    [Range(1, 10_000)]
    public int ViewportHeight { get; set; } = 1080;

    /// <summary>Screenshot the whole scrollable page on failure, not just the viewport.</summary>
    public bool FullPageScreenshot { get; set; }

    /// <summary>A pause before each action, in milliseconds. Unset: 0 when Headless, 2000 otherwise.</summary>
    [Range(0, int.MaxValue)]
    public int? SlowMo { get; set; }

    /// <summary>Leave the page open in the Playwright inspector after the flows (Headless: false, in a terminal).</summary>
    public bool KeepOpen { get; set; }

    /// <summary>
    /// Save the context's cookies and localStorage here after every flow passed, for other sessions to load with
    /// LoadStorageStatePath. sessionStorage is not saved.
    /// </summary>
    public string? SaveStorageStatePath { get; set; }

    /// <summary>Start in a fresh context seeded from this file, already logged in. The file must exist.</summary>
    public string? LoadStorageStatePath { get; set; }

    /// <summary>
    /// Run in a fresh context of its own, disposed afterwards, so parallel sessions can neither overwrite each other's
    /// login nor wait for each other to render. False shares the browser's default context, e.g. to reuse the logins
    /// of a local Chrome.
    /// </summary>
    public bool IsolateContext { get; set; } = true;

    /// <summary>
    /// Make matchMedia report a mouse, for a Chrome that reports none (a headless Chrome started by hand), so
    /// responsive apps render their desktop layout. CSS media queries need the Chrome flags the probe's warning names.
    /// </summary>
    public bool EmulateDesktopPointer { get; set; }

    /// <summary>
    /// The CDP endpoint of the Chrome to run in, e.g. <c>ws://chrome.&lt;namespace&gt;.svc.cluster.local:3000?token=...</c>
    /// or <c>http://localhost:9222</c>. Unset: the one in browser-defaults.yaml. When it is on this machine and
    /// nothing answers there, the probe starts Chrome.
    /// </summary>
    public string? BrowserUrl { get; set; }

    /// <summary>The Chrome the probe starts for a BrowserUrl on this machine. Unset: found in the usual places.</summary>
    public string? BrowserExecutablePath { get; set; }
}
