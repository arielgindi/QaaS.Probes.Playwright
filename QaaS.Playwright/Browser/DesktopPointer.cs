using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace QaaS.Playwright.Browser;

/// <summary>
/// A headless Chrome started by hand reports no mouse, so responsive apps render their mobile layout: MUI date pickers,
/// for one, become read-only fields with other accessible names than in a desktop browser.
/// </summary>
internal static class DesktopPointer
{
    /// <summary>The Chrome flags that make it report a mouse, for CSS media queries too.</summary>
    public const string LaunchFlags =
        "--blink-settings=primaryPointerType=4,availablePointerTypes=4,primaryHoverType=2,availableHoverTypes=2";

    // Answers matchMedia's single pointer and hover queries as a mouse would. Compound queries and CSS are unchanged.
    private const string MatchMediaShim = """
        (() => {
          const mouse = { 'pointer:fine': true, 'pointer:coarse': false, 'pointer:none': false,
                          'hover:hover': true, 'hover:none': false };
          const matchMedia = window.matchMedia.bind(window);
          window.matchMedia = query => {
            const list = matchMedia(query);
            const feature = String(query).replace('@media', '').replace(/[\s()]/g, '').replace(/^any-/, '');
            if (!(feature in mouse)) return list;
            return new Proxy(list, { get: (target, name) => name === 'matches' ? mouse[feature]
              : typeof target[name] === 'function' ? target[name].bind(target) : target[name] });
          };
        })();
        """;

    private static int _warned;

    /// <summary>Makes every document the page loads report a mouse to matchMedia.</summary>
    public static Task EmulateAsync(IPage page) => page.AddInitScriptAsync(MatchMediaShim);

    /// <summary>Warns, once per process, when the browser reports no mouse.</summary>
    public static async Task WarnIfMissingAsync(IPage page, ILogger logger)
    {
        if (Volatile.Read(ref _warned) == 1 || !await ReportsNoMouseAsync(page)) return;
        if (Interlocked.Exchange(ref _warned, 1) == 1) return;

        logger.LogWarning(
            "The browser reports no mouse (pointer: none), as a headless Chrome started by hand does, so responsive " +
            "apps render their mobile layout. Start Chrome with {LaunchFlags}, or set " +
            "ProbeConfiguration.EmulateDesktopPointer: true.", LaunchFlags);
    }

    private static async Task<bool> ReportsNoMouseAsync(IPage page)
    {
        try
        {
            return await page.EvaluateAsync<bool>("() => matchMedia('(pointer: none)').matches");
        }
        catch (PlaywrightException)
        {
            return false; // Only a diagnostic; it must never fail the run.
        }
    }
}
