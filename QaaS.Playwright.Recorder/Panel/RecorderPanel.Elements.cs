using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.State;

namespace QaaS.Playwright.Recorder.Panel;

/// <summary>
/// The Elements view as a browser's developer tools have it: inspect mode (Ctrl+Shift+C, in the app or the panel), in
/// which the tree follows the pointer in the app and a click there picks the element under it; the element the panel's
/// pointer is on, and each match of its search, marked in the app by the capture script's overlay; and, for the element
/// picked, the lines each step on it would write before it is made.
/// </summary>
internal sealed partial class RecorderPanel
{
    // The worker's own: the page an element of the tree is marked in, the step the picked element would replace the
    // element of, the check it would add, and the lines each step on it would write, while nothing changes.
    private IPage? _marked;
    private string? _elementStep;
    private string? _elementCheck;
    private string? _previewKey;
    private IReadOnlyList<PanelPreview> _previews = [];

    // The panel's pointer on an element of the tree marks it in the app, and scrolls it into view there; any other
    // highlight takes the mark away. Playwright's own highlight is left for the others: null hides it.
    private async Task<PanelCommand?> MarkAsync(PanelCommand command)
    {
        int? node = command is { Type: "highlight", Action: null, Node: { } id } ? id : null;
        if (node is null && _marked is null) return command;
        var page = node is null ? _marked : _session.PageNamed(command.Frozen == true ? _session.Frozen?.Page ?? "page" : _elementsPage);
        if (page is { IsClosed: false })
            await PageElements.CallAsync(page, new Dictionary<string, object?> { ["op"] = "mark", ["hover"] = node, ["scroll"] = true });
        _marked = node is null ? null : page;
        return node is null ? command : null;
    }

    // The search's matches, marked in the app; none when it is cleared or the view closes.
    private async Task MarkMatchesAsync(PanelCommand command)
    {
        if (_session.PageNamed(_elementsPage) is not { } page) return;
        await PageElements.CallAsync(page, new Dictionary<string, object?>
        {
            ["op"] = "mark", ["matches"] = command.Matches ?? [], ["current"] = command.Current,
        });
    }

    private async Task InspectAsync(bool on)
    {
        if (_session.PageNamed(_session.CurrentPage()) is not { } page) return;
        // The pointer goes there.
        if (on) await page.BringToFrontAsync();
        await PageElements.CallAsync(page, new { op = "inspect", on });
    }

    // What the app in inspect mode told: in or out of it, the element under the pointer, or the one a click picked, with
    // the elements around it, for the tree to open to it.
    private async Task ShowInspectedAsync(string alias, string json)
    {
        if (JsonNode.Parse(json) is not JsonObject told) return;
        if (told["node"] is not null) _elementsPage = alias;
        var message = new JsonObject
        {
            ["kind"] = "inspect", ["page"] = alias, ["on"] = told["on"]?.GetValue<bool>() == true,
            ["node"] = told["node"]?.DeepClone(), ["picked"] = told["picked"]?.GetValue<bool>() == true,
            ["data"] = told["tree"]?.DeepClone(),
        };
        await _page.EvaluateAsync<bool>(ElementsFunction, message.ToJsonString());
    }

    // The step the picked element would act on instead, and the check it would add, as the panel has them selected.
    private void PreviewFor(PanelCommand command)
    {
        (_elementStep, _elementCheck, _refresh) = (command.Action, command.Check, true);
    }

    // The picked element as the inspector shows it, with the lines each step on it would write.
    private PanelElement PickedView(Picked picked, PanelSnapshot shown)
    {
        var view = PanelSnapshot.Element(picked.Node, picked.Page, picked.Description, picked.Gone, picked.Fillable);
        var checks = ChecksOf(picked);
        var check = checks.FirstOrDefault(each => each.Kind == _elementCheck) ?? checks.ElementAtOrDefault(picked.Check) ?? checks.FirstOrDefault();
        var before = _session.Snapshot.Part?.Phase == PartPhase.Recording && _part is { } part ? part.Part.Before : null;
        var key = string.Join('|', _session.Log.Version, picked.Node, picked.Description.Target?.Chosen, _elementStep, check?.Kind, before,
            Request.ClassName);
        if (key != _previewKey) (_previews, _previewKey) = (Previews(picked, shown, check, before), key);
        var canUse = _previews.Any(preview => preview.Kind == "use");
        return view with
        {
            Previews = _previews,
            Checks = [.. checks.Select(each => new PanelCheck(each.Kind, each.Name) { Value = each.Value })],
            Check = check is null ? 0 : checks.IndexOf(check),
            Primary = canUse && picked.Related ? "use" : picked.Fillable ? "fill" : "click",
        };
    }

    // The checks that fit the element as the page found them; for one not on the page now, that it shows, and its text.
    private static List<PickedCheck> ChecksOf(Picked picked)
    {
        if (picked.Checks is { Count: > 0 } checks) return checks;
        List<PickedCheck> fallback = [new PickedCheck { Kind = "visible", Name = "Visible" }];
        if (picked.Description.Target?.Text is { Length: > 0 } text)
            fallback.Insert(0, new PickedCheck { Kind = "text", Name = "Has text", Value = text });
        return fallback;
    }

    // Each step as the recorder would write it: the selected step acting on the element instead, and a click, a fill or
    // a check added where the next action goes, in place of the part being recorded again, if any. Each is told by the
    // lines it adds to the flow and those it takes out, as a check is written with the action before it.
    private List<PanelPreview> Previews(Picked picked, PanelSnapshot shown, PickedCheck? check, string? before)
    {
        var recording = shown.Recording;
        var code = shown.View.Code;
        var previews = new List<PanelPreview>();
        if (_elementStep is { } id && recording.Actions.FirstOrDefault(action => action.Id == id) is { Target: not null } step)
        {
            var retargeted = Copy(step);
            CaptureLog.Retarget(retargeted, picked.Description);
            if (Preview("use", code, recording with { Actions = [.. recording.Actions.Select(action => action.Id == id ? retargeted : action)] }) is { } use)
                previews.Add(use);
        }

        var at = before is null ? -1 : recording.Actions.ToList().FindIndex(action => action.Id == before);
        List<PanelCommand> added = [new("addClick", null, null, null)];
        if (picked.Fillable) added.Add(new PanelCommand("addFill", null, null, null) { Value = "text" });
        if (check is not null) added.Add(new PanelCommand("addCheck", null, null, null) { Check = check.Kind });
        foreach (var command in added)
        {
            var action = NewAction(command, picked);
            List<CapturedAction> actions = [.. recording.Actions];
            var index = at < 0 ? actions.Count : at;
            (action.Added, action.T) = (true, index > 0 ? actions[index - 1].T : 0);
            actions.Insert(index, action);
            if (Preview(command.Type[3..].ToLowerInvariant(), code, recording with { Actions = actions }) is { } preview)
                previews.Add(preview);
        }

        return previews;
    }

    // The lines the changed recording writes that the flow does not, and those it no longer writes, without their indent.
    private PanelPreview? Preview(string kind, string code, Recording changed)
    {
        string after;
        try
        {
            after = FlowCodeGenerator.PlanAndWrite(changed, Request.ClassName, _namespace ?? "Flows", Request.AutoChecks).Flow.Code;
        }
        catch (Exception)
        {
            // A step the writer cannot write has no lines to show.
            return null;
        }

        var (allGone, allCome) = LineDiff(code.Split('\n'), after.Split('\n'));
        // The statements, without the methods, comments, settings and auto checks around them, unless that is all.
        var (gone, come) = (allGone.Where(Statement).ToList(), allCome.Where(Statement).ToList());
        if (come.Count == 0) (gone, come) = (allGone, allCome);
        if (come.Count == 0) return null;
        var indent = gone.Concat(come).Where(line => line.Trim().Length > 0)
            .Select(line => line.Length - line.TrimStart().Length).DefaultIfEmpty().Min();
        string Join(List<string> lines) => string.Join('\n', lines.Select(line => line.Length >= indent ? line[indent..] : line.TrimStart()));
        return new PanelPreview(kind, Join(come)) { Was = gone.Count > 0 ? Join(gone) : null };
    }

    // A line that does something, rather than declares, documents, calls a step's method or checks by itself.
    private static bool Statement(string line)
    {
        var text = line.Trim();
        return text.Length > 1 && !text.StartsWith("///", StringComparison.Ordinal) && !text.StartsWith("using ", StringComparison.Ordinal)
               && !text.StartsWith("private ", StringComparison.Ordinal)
               && !text.StartsWith("public ", StringComparison.Ordinal) && !text.StartsWith("return ", StringComparison.Ordinal)
               && !text.EndsWith("// auto check", StringComparison.Ordinal) && !StepCall().IsMatch(text);
    }

    [GeneratedRegex(@"^(var \w+ = )?await \w+Async\(\w+\);$")]
    private static partial Regex StepCall();

    // The lines only the one has and only the other has, by their longest common sequence.
    private static (List<string> Gone, List<string> Come) LineDiff(string[] before, string[] after)
    {
        var (start, end) = (0, 0);
        while (start < before.Length && start < after.Length && before[start] == after[start]) start++;
        while (end < before.Length - start && end < after.Length - start && before[^(end + 1)] == after[^(end + 1)]) end++;
        var old = before[start..^end];
        var fresh = after[start..^end];
        var common = new int[old.Length + 1, fresh.Length + 1];
        for (var i = old.Length - 1; i >= 0; i--)
            for (var j = fresh.Length - 1; j >= 0; j--)
                common[i, j] = old[i] == fresh[j] ? common[i + 1, j + 1] + 1 : Math.Max(common[i + 1, j], common[i, j + 1]);
        var (gone, come) = (new List<string>(), new List<string>());
        var (a, b) = (0, 0);
        while (a < old.Length || b < fresh.Length)
        {
            if (a < old.Length && b < fresh.Length && old[a] == fresh[b]) (a, b) = (a + 1, b + 1);
            else if (b < fresh.Length && (a == old.Length || common[a, b + 1] >= common[a + 1, b])) come.Add(fresh[b++]);
            else gone.Add(old[a++]);
        }

        return (gone, come);
    }

    private static CapturedAction Copy(CapturedAction action) =>
        JsonSerializer.Deserialize<CapturedAction>(JsonSerializer.Serialize(action, Recording.Json), Recording.Json)!;

    /// <summary>A check the capture script found to fit the element: its kind, how it reads, and the value it expects.</summary>
    private sealed class PickedCheck
    {
        public string Kind { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Value { get; set; }

        /// <summary>For a count: the locators that find every item the element is one of.</summary>
        public CapturedTarget? Group { get; set; }
    }
}
