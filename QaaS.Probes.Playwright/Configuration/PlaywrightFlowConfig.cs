using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace QaaS.Probes.Playwright.Configuration;

/// <summary>
/// Configuration for the PlaywrightFlowProbe, bound from YAML <c>ProbeConfiguration</c>.
/// </summary>
public class PlaywrightFlowConfig
{
    [Required]
    public string BaseUrl { get; set; } = null!;

    /// <summary>
    /// When true (default), the browser runs invisibly. Set to false to watch the flow
    /// execute — this also auto-enables SlowMo (1s) and disables asset blocking.
    /// </summary>
    [DefaultValue(true)]
    public bool Headless { get; set; } = true;

    /// <summary>Block images, fonts, and icons in headless mode for faster execution.</summary>
    [DefaultValue(true)]
    public bool BlockAssets { get; set; } = true;

    /// <summary>Disable CSS transitions and animations in headless mode.</summary>
    [DefaultValue(true)]
    public bool DisableAnimations { get; set; } = true;

    [DefaultValue(30000)]
    public int DefaultTimeout { get; set; } = 30000;

    /// <summary>Delay between steps in ms. Defaults to 1000 when Headless is false.</summary>
    [DefaultValue(0)]
    public int SlowMo { get; set; }

    /// <summary>Keep the browser open after completion (only works with Headless: false).</summary>
    [DefaultValue(false)]
    public bool KeepOpen { get; set; }

    /// <summary>Flows that run once before the main flows (login, cookie consent, etc).
    /// Share the same browser context so cookies carry over.</summary>
    public string[]? SetupFlows { get; set; }

    /// <summary>Main flows to execute. Each is a C# class name implementing IPlaywrightFlow.</summary>
    public string[]? Flows { get; set; }
}
