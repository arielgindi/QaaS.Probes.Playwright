using System.Text.Json;
using Microsoft.Playwright;
using QaaS.Playwright.Browser;
using QaaS.Playwright.Recorder.Capture;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>The recorder in a headless Chrome on a local page: what its overlay shows under the pointer.</summary>
[TestFixture]
[Category("EndToEnd")]
public class RecordingSessionTests
{
    private string _folder = null!;

    [SetUp]
    public void CreateFolder()
    {
        if (ChromeExecutable.Find() is null) Assert.Ignore("Google Chrome is not installed.");
        _folder = Directory.CreateTempSubdirectory("qaas-recorder-session-").FullName;
    }

    [TearDown]
    public void DeleteFolder() => Directory.Delete(_folder, recursive: true);

    [Test]
    public async Task Overlay_ShowsWhyTheIdLost_AndPlaywrightNeverSeesIt()
    {
        // A responsive layout keeps a hidden copy of the button, with the same id, for another width.
        await using var session = await StartAsync("""
            <button id="save-order" style="display: none">Save order</button>
            <button id="save-order">Save order<span><svg width="12" height="12"></svg></span></button>
            """);

        await session.Page.Locator("button:visible").HoverAsync();

        var label = await OverlayTextAsync(session.Page, until: "id: found");
        var seenByPlaywright = await session.Page.GetByText("found 2").CountAsync();
        Assert.Multiple(() =>
        {
            Assert.That(label, Does.StartWith("button").And.Contain("\"Save order\""));
            Assert.That(label, Does.Contain("id: found 2 (1 hidden)"));
            Assert.That(seenByPlaywright, Is.Zero, "Playwright's selectors see the overlay");
        });
    }

    private async Task<RecordingSession> StartAsync(string body)
    {
        var page = Path.Combine(_folder, "page.html");
        await File.WriteAllTextAsync(page, $"<!doctype html><meta charset=\"utf-8\"><body>{body}</body>");
        // Never the person's own login.
        return await RecordingSession.StartAsync(new RecordingOptions(new Uri(page).AbsoluteUri)
        {
            LoadStorageStatePath = null, SaveStorageStatePath = null, BrowserArgs = ["--headless=new"],
        }, _ => { });
    }

    // The overlay's text, one line per node, read through Chrome's DevTools protocol, which sees into closed shadow roots.
    private static async Task<string> OverlayTextAsync(IPage page, string until)
    {
        var devTools = await page.Context.NewCDPSessionAsync(page);
        var text = "";
        for (var attempt = 0; attempt < 50 && !text.Contains(until, StringComparison.Ordinal); attempt++)
        {
            await Task.Delay(100);
            var document = await devTools.SendAsync("DOM.getDocument", new() { ["depth"] = -1, ["pierce"] = true });
            text = string.Join('\n', TextsIn(document!.Value.GetProperty("root"), inOverlay: false));
        }

        return text;
    }

    private static IEnumerable<string> TextsIn(JsonElement node, bool inOverlay)
    {
        inOverlay |= node.GetProperty("nodeName").GetString() == "QAAS-RECORDER-OVERLAY";
        if (inOverlay && node.GetProperty("nodeType").GetInt32() == 3) yield return node.GetProperty("nodeValue").GetString()!;
        foreach (var kind in (string[])["children", "shadowRoots"])
            if (node.TryGetProperty(kind, out var children))
                foreach (var text in children.EnumerateArray().SelectMany(child => TextsIn(child, inOverlay)))
                    yield return text;
    }
}
