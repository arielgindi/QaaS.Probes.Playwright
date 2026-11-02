using System.Runtime.CompilerServices;
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

    // Answers matchMedia's single pointer and hover queries as a mouse would, once per document, and marks the document
    // as doing so. Compound queries and CSS are unchanged.
    private const string MatchMediaShim = """
        (() => {
          const marker = Symbol.for('qaas.desktopPointer');
          if (window[marker]) return;
          window[marker] = true;
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

    // The same, but only in a pop-up whose opener's document has it: a page the run opened, not any other page.
    private const string PopupShim = $$"""
        (() => {
          try { if (!window.opener?.[Symbol.for('qaas.desktopPointer')]) return; } catch { return; }
          {{MatchMediaShim}}
        })();
        """;

    // Every page of a Chrome reports the same pointer, so each connection asks once.
    private static readonly ConditionalWeakTable<IBrowser, Task<bool>> NoMouse = new();

    // A context keeps its init scripts as long as its connection, so each gets the pop-ups' script once.
    private static readonly ConditionalWeakTable<IBrowserContext, Task> PopupsShimmed = new();

    /// <summary>Makes every document the page loads report a mouse to matchMedia.</summary>
    public static Task EmulateAsync(IPage page) => page.AddInitScriptAsync(MatchMediaShim);

    /// <summary>Makes every document of every page of the context report a mouse to matchMedia.</summary>
    public static Task EmulateAsync(IBrowserContext context) => context.AddInitScriptAsync(MatchMediaShim);

    /// <summary>
    /// Makes a pop-up of a page that reports a mouse to matchMedia report one too, from its first document on, which a
    /// pop-up's own init script comes too late for. Other pages of the context are left as they are.
    /// </summary>
    public static Task EmulateInPopupsAsync(IBrowserContext context) =>
        PopupsShimmed.GetValue(context, context => context.AddInitScriptAsync(PopupShim));

    /// <summary>Warns when the browser reports no mouse, in every run, so every report says so.</summary>
    public static async Task WarnIfMissingAsync(IPage page, ILogger logger)
    {
        if (!await NoMouse.GetValue(page.Context.Browser!, _ => ReportsNoMouseAsync(page))) return;

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
