using System.Text.Json;
using Microsoft.Playwright;

namespace QaaS.Playwright.Recorder.Panel;

/// <summary>A window's place on the screen, in the screen's pixels.</summary>
internal sealed record WindowBounds(int Left, int Top, int Width, int Height);

/// <summary>
/// Places the panel's window beside the recorded one, and a test run's over it, through Chrome's own window commands
/// (CDP).
/// </summary>
internal static class PanelWindow
{
    private const int WantedWidth = 1200;
    private const int LeastWidth = 640;

    /// <summary>
    /// Moves the panel's window beside the recorded window. Returns whether it is beside it, rather than over its
    /// right part, which it is when the screen has no room beside it.
    /// </summary>
    public static async Task<bool> PlaceAsync(IPage recorded, IPage panel)
    {
        try
        {
            var (_, app) = await WindowOfAsync(recorded);
            var (panelWindow, _) = await WindowOfAsync(panel);
            var screen = await panel.EvaluateAsync<int[]>(
                "() => [screen.availLeft, screen.availTop, screen.availWidth, screen.availHeight]");
            var (bounds, beside) = Arrange(app, new WindowBounds(screen[0], screen[1], screen[2], screen[3]));
            await MoveAsync(panel, panelWindow, bounds);
            return beside;
        }
        catch (Exception failure) when (failure is PlaywrightException or KeyNotFoundException or InvalidOperationException)
        {
            return false; // Where it opened will do.
        }
    }

    /// <summary>
    /// Moves the window of <paramref name="page"/> onto the recorded window, in its place and at its size, e.g. the
    /// test run's: it shows the app where the person recorded it, and leaves the panel beside it in view.
    /// </summary>
    public static async Task PlaceOverAsync(IPage recorded, IPage page)
    {
        try
        {
            var (_, app) = await WindowOfAsync(recorded);
            var (window, _) = await WindowOfAsync(page);
            await MoveAsync(page, window, app);
        }
        catch (Exception failure) when (failure is PlaywrightException or KeyNotFoundException or InvalidOperationException)
        {
            // Where it opened will do.
        }
    }

    private static async Task MoveAsync(IPage page, int window, WindowBounds bounds)
    {
        var session = await page.Context.NewCDPSessionAsync(page);
        try
        {
            await session.SendAsync("Browser.setWindowBounds", new()
            {
                ["windowId"] = window,
                ["bounds"] = new Dictionary<string, object>
                {
                    ["left"] = bounds.Left, ["top"] = bounds.Top, ["width"] = bounds.Width, ["height"] = bounds.Height,
                },
            });
        }
        finally
        {
            await session.DetachAsync();
        }
    }

    /// <summary>
    /// Where the panel goes: beside the recorded window where the screen has room for it, else at the screen's right
    /// edge, over the recorded window, which Playwright sizes to the recording's viewport and may fill the screen.
    /// </summary>
    public static (WindowBounds Bounds, bool Beside) Arrange(WindowBounds app, WindowBounds screen)
    {
        var screenRight = screen.Left + screen.Width;
        var roomRight = screenRight - (app.Left + app.Width);
        var roomLeft = app.Left - screen.Left;
        var top = Math.Max(app.Top, screen.Top);
        var height = Math.Min(app.Height, screen.Top + screen.Height - top);
        if (roomRight >= LeastWidth) return (new WindowBounds(app.Left + app.Width, top, Math.Min(roomRight, WantedWidth), height), true);
        if (roomLeft >= LeastWidth)
        {
            var width = Math.Min(roomLeft, WantedWidth);
            return (new WindowBounds(app.Left - width, top, width, height), true);
        }

        var docked = Math.Clamp(screen.Width * 45 / 100, Math.Min(LeastWidth, screen.Width), WantedWidth);
        return (new WindowBounds(screenRight - docked, screen.Top, docked, screen.Height), false);
    }

    private static async Task<(int Id, WindowBounds Bounds)> WindowOfAsync(IPage page)
    {
        var session = await page.Context.NewCDPSessionAsync(page);
        try
        {
            var window = await session.SendAsync("Browser.getWindowForTarget")
                         ?? throw new PlaywrightException("Chrome did not tell the page's window.");
            var bounds = window.GetProperty("bounds");
            int Read(string name) => bounds.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
            return (window.GetProperty("windowId").GetInt32(), new WindowBounds(Read("left"), Read("top"), Read("width"), Read("height")));
        }
        finally
        {
            await session.DetachAsync();
        }
    }
}
