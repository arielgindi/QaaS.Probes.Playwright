using System.Text.Json;
using Microsoft.Playwright;
using QaaS.Playwright.Browser;
using QaaS.Playwright.Recorder;
using QaaS.Playwright.Recorder.Capture;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>
/// The recorder in a headless Chrome on a local page: what its overlay shows under the pointer, and which element a
/// click is recorded on.
/// </summary>
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

    [Test]
    public async Task ClickableCardsAndCarouselWrappers_AreRecordedByTheStableIdInside()
    {
        var actions = 0;
        await using var session = await StartAsync("""
            <style>
              .slick-track { display: flex; gap: 16px; }
              .slick-slide { width: 200px; flex: none; }
              .card { display: inline-block; width: 100%; height: 60px; overflow: hidden; cursor: pointer; }
            </style>
            <div class="slick-track">
              <div class="slick-slide"><div><div id="card-orders" class="card"><h3>Orders</h3></div></div></div>
              <div class="slick-slide" style="cursor: pointer"><div><div id="card-tickets"><h3>Tickets</h3></div></div></div>
            </div>
            <div id="tile-reports" style="cursor: pointer; width: 200px"><span>Reports</span> <span>2 new</span></div>
            <p id="opened"></p>
            <script>
              // What each card opens, as an app's one listener does; a click that changes nothing is no step.
              document.addEventListener('click', (event) => { opened.textContent = event.target.textContent; });
            </script>
            """, news => { if (news is CapturedAction) Interlocked.Increment(ref actions); });
        var page = session.Page;

        // Just below the card, on the wrapper the carousel put around it.
        var wrapper = (await page.Locator("#card-orders >> xpath=..").BoundingBoxAsync())!;
        var (x, y) = (wrapper.X + 20, wrapper.Y + wrapper.Height - 1);
        var hitsWrapper = await page.EvaluateAsync<bool>(
            "({ x, y }) => document.elementFromPoint(x, y) === document.getElementById('card-orders').parentElement",
            new { x = (double)x, y = (double)y });
        await page.Mouse.ClickAsync(x, y);
        // The slide is what looks clickable; the element inside it has the id.
        await page.GetByText("Tickets").ClickAsync();
        await page.GetByText("Reports").ClickAsync();
        for (var wait = 0; wait < 50 && Volatile.Read(ref actions) < 3; wait++) await Task.Delay(100);
        var code = FlowCodeGenerator.Generate(await session.StopAsync(), "OpenCards", "Flows").Code;

        Assert.Multiple(() =>
        {
            Assert.That(hitsWrapper, Is.True, "the first click must land on the wrapper");
            Assert.That(code, Does.Contain("""await page.Locator("[id='card-orders']").ClickAsync();"""));
            Assert.That(code, Does.Contain("""await page.Locator("[id='card-tickets']").ClickAsync();"""));
            Assert.That(code, Does.Contain("""await page.Locator("[id='tile-reports']").ClickAsync();"""));
        });
    }

    [Test]
    public async Task Paused_RecordsNothing_AndResumesOnThePageTheAppWentTo()
    {
        // An app that tests with a date of its own, a minute behind: what is done after the pause is still recorded.
        await File.WriteAllTextAsync(Path.Combine(_folder, "second.html"),
            "<!doctype html><script>const real = Date.now; Date.now = () => real() - 60000;</script><body><button id=\"last\">Last</button> <input name=\"later\"> <iframe srcdoc=\"<input name=inner>\"></iframe>");
        var notRecorded = new List<string>();
        await using var session = await StartAsync("""
            <button id="first">First</button> <input name="note"> <a href="second.html">Second page</a>
            """, news => { if (news is NotRecordedWhilePaused ignored) lock (notRecorded) notRecorded.Add(ignored.What); });
        var page = session.Page;

        await page.Locator("#first").ClickAsync();
        // Typed just before the panel's Pause, which the page learns of a moment later: it was typed before the pause.
        await page.Locator("[name='note']").FillAsync("typed before");
        session.TogglePause();
        await UntilAsync(() => session.State == RecordingState.Paused);
        await page.Locator("[name='note']").FillAsync("not recorded");
        var toast = await OverlayTextAsync(page, until: "was not recorded");
        await page.GetByText("Second page").ClickAsync();
        var overlay = await OverlayTextAsync(page, until: "nothing you do is recorded");
        await page.Keyboard.PressAsync("F8");
        await UntilAsync(() => session.State == RecordingState.Recording);
        await page.Locator("#last").ClickAsync();
        // With the focus in a frame, which records nothing, F8 pauses and resumes all the same.
        await page.FrameLocator("iframe").Locator("[name='inner']").ClickAsync();
        await page.Keyboard.PressAsync("F8");
        await UntilAsync(() => session.State == RecordingState.Paused);
        await page.Keyboard.PressAsync("F8");
        await UntilAsync(() => session.State == RecordingState.Recording);
        // Stopped as soon as it is typed, as Enter or Ctrl+C may: what is being typed is part of the recording.
        await page.Locator("[name='later']").PressSequentiallyAsync("just typed");
        var recording = await session.StopAsync();

        var flow = FlowCodeGenerator.Generate(recording, "PausedFlow", "Flows");
        Assert.Multiple(() =>
        {
            Assert.That(overlay, Does.Contain("Paused:\nnothing you do is recorded; F8 resumes"), "the new page shows the pause too");
            Assert.That(toast, Does.Contain("Paused: that typing was not recorded · F8 resumes"));
            Assert.That(notRecorded, Is.EqualTo(new[] { "typing" }), "the console says it once a pause");
            Assert.That(recording.Actions.Select(action => action.Target?.Id ?? action.Kind),
                Is.EqualTo(new[] { "first", "fill", "navigate", "last", "fill" }), "what was done while paused is not recorded");
            Assert.That(recording.Actions[^1].Value, Is.EqualTo("just typed"));
            Assert.That(recording.Actions[1].Value, Is.EqualTo("typed before"));
            Assert.That(recording.Actions[2].Url, Does.EndWith("/second.html"));
            Assert.That(flow.Checks.Select(check => check.Message), Has.Some.StartWith("the recording was paused"));
        });
    }

    [Test]
    public async Task ABusyIndicatorThatCameAndWent_IsWaitedFor_AndOneThatStays_IsNot()
    {
        // A spinner as MUI's CircularProgress, while "Refresh" loads; "Upload" leaves its progress bar on the page.
        await using var session = await StartAsync("""
            <button id="refresh" onclick="busy(400)">Refresh</button> <button id="upload" onclick="busy(0)">Upload</button>
            <script>
              function busy(ms) {
                const spinner = document.body.appendChild(document.createElement('span'));
                spinner.setAttribute('role', 'progressbar');
                spinner.style.cssText = 'display: inline-block; width: 40px; height: 40px';
                if (ms) setTimeout(() => spinner.remove(), ms);
              }
            </script>
            """);
        var page = session.Page;

        await page.Locator("#refresh").ClickAsync();
        await Task.Delay(1000);
        await page.Locator("#upload").ClickAsync();
        await Task.Delay(1000);
        var recording = await session.StopAsync();

        var code = FlowCodeGenerator.Generate(recording, "Busy", "Flows").Code;
        Assert.Multiple(() =>
        {
            Assert.That(recording.Actions.Select(action => action.Effects.Any(effect => effect.Kind == "busy")),
                Is.EqualTo(new[] { true, false }));
            Assert.That(code.Split('\n').Count(line => line.Contains("ToHaveCountAsync(0", StringComparison.Ordinal)), Is.EqualTo(1));
            Assert.That(code, Does.Contain(
                "await Expect(page.GetByRole(AriaRole.Progressbar)).ToHaveCountAsync(0, new() { Timeout = 30_000 });"));
        });
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        for (var wait = 0; wait < 50 && !condition(); wait++) await Task.Delay(100);
        Assert.That(condition(), Is.True, "timed out");
    }

    private async Task<RecordingSession> StartAsync(string body, Action<object>? onNews = null)
    {
        var page = Path.Combine(_folder, "page.html");
        await File.WriteAllTextAsync(page, $"<!doctype html><meta charset=\"utf-8\"><body>{body}</body>");
        // Never the person's own login.
        return await RecordingSession.StartAsync(new RecordingOptions(new Uri(page).AbsoluteUri)
        {
            LoadStorageStatePath = null, SaveStorageStatePath = null, BrowserArgs = ["--headless=new"],
        }, onNews ?? (_ => { }));
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
