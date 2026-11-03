using Microsoft.Playwright;

namespace QaaS.Playwright.Recorder.Capture;

/// <summary>What the capture script finds to locate an element, as it describes the element of an action.</summary>
internal sealed class ElementDescription
{
    /// <summary>The dialog the element is in, which the flow finds it inside.</summary>
    public CapturedDialog? Dialog { get; set; }

    public List<string>? OpenDialogs { get; set; }
    public CapturedTarget? Target { get; set; }
}

/// <summary>
/// A page's elements as they were when the person pressed F4: the tree the panel shows, and how to locate each element
/// a click may target, found then, as a menu that closed since is gone.
/// </summary>
internal sealed record FrozenElements(
    string Page, DateTimeOffset At, string Tree, IReadOnlyDictionary<int, ElementDescription> Described);

/// <summary>Calls the capture script in a page: its elements for the panel, and the recording's state for its overlay.</summary>
internal static class PageElements
{
    private const string Call =
        "command => typeof window.__qaasRecorder === 'function' ? window.__qaasRecorder(command) : null";

    /// <summary>The script's answer, as JSON; null when the page is closed or loading, or answered nothing.</summary>
    public static async Task<string?> CallAsync(IPage page, object command)
    {
        try
        {
            return await page.EvaluateAsync<string?>(Call, command);
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }
}
