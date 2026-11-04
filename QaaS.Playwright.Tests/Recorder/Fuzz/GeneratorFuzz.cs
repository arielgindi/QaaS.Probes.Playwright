using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using QaaS.Playwright.Recorder;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Generation;
using QaaS.Playwright.Recorder.Login;
using QaaS.Playwright.Recorder.TestRun;

namespace QaaS.Playwright.Tests.Recorder.Fuzz;

/// <summary>
/// Fuzzes the generator with no browser. Each mutant is a real recording of the tests, changed as a person, a paused
/// recording or an older recorder may change it: actions dropped, repeated, moved or cut off; one field at a time null,
/// empty or odd; pauses, pop-ups, tabs and navigations put in anywhere; the panel's edits on any action; loops, checks
/// and secrets mixed in; another start address. Each must give a flow without throwing, with every action written or
/// said to check by hand, lines that match its code, code that builds with every warning an error, and YAML that binds
/// to its settings. A failure is cut down to the smallest recording that still fails the same way.
/// </summary>
internal static partial class GeneratorFuzz
{
    /// <summary>The namespace of the flows, as the fuzz project's <c>Flows</c> folder gives it.</summary>
    public const string Namespace = "FuzzFlows.Flows";

    private static readonly string[] Kinds =
        ["click", "dblclick", "fill", "check", "uncheck", "choose", "press", "upload", "drag", "navigate", "newTab", "expect"];

    private static readonly string[] Widgets =
    [
        "select", "select-open", "autocomplete", "autocomplete-button", "native", "option", "radio", "checkbox", "switch", "tab",
        "menuitem", "date", "text", "textarea", "bogus",
    ];

    private static readonly string[] EffectKinds =
        ["dialogOpened", "dialogClosed", "alert", "tabSelected", "settled", "changed", "busy", "urlChanged", "popup", "jsDialog"];

    private static readonly string[] CheckKinds =
        ["visible", "hidden", "text", "contains", "value", "checked", "unchecked", "enabled", "disabled", "count", "row", "url", "bogus"];

    // Text that a literal, a name, a comment, a regex or YAML must take as it is.
    private static readonly string[] Odd =
    [
        "", " ", "\"", "\\", "{0}", "{{x}}", "*/", "/*", "line\nbreak", "</summary>", "<c>x</c>", "&amp;", "a'b", "${HOME}", "#",
        "$\"x\"", "\u200fx\u200e", "___", "😀", new string('x', 300), "123", "-1", "0", "1.5", "null",
        "undefined", "[object Object]", "Configuration", "page", "class", "Save \"draft\"", "\t", "\u0001", "%", "\u2028",
        "Order 5521 saved", "plus New", "Close HD-1041", "(", "[a-z]+", "Price $12.50", "dd/MM/yyyy", "06/11/2026",
    ];

    private static readonly string[] Addresses =
    [
        "https://app.test/start", "https://app.test/start/orders/5", "https://app.test/", "https://app.test/start?x=1#top",
        "https://app.test/#/orders/12", "https://other.test/", "not an address", "", "about:blank", "file:///tmp/x.html",
        "https://app.test/{x}\"", "http://[::1]:8080/a b", "https://app.test/start/" + new string('a', 200),
    ];

    // ── Seeds ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A recording to mutate, by its name, as the JSON a recording file holds.</summary>
    public sealed record Seed(string Name, string Json);

    /// <summary>
    /// Every real recording of the tests: the golden ones, those of real sites, the build test's samples with the fuzz's
    /// own regressions; and those in the folder <c>QAAS_FUZZ_SEEDS</c> names, as the picker and site tests record them.
    /// </summary>
    public static IEnumerable<Seed> Seeds()
    {
        foreach (var folder in new[] { "Golden", "RealSites" })
            foreach (var path in Directory.GetFiles(Path.Combine(Here(), "..", folder), "*.recording.json").Order(StringComparer.Ordinal))
                yield return new Seed($"{folder}/{Path.GetFileName(path)[..^".recording.json".Length]}", File.ReadAllText(path));
        foreach (var (name, recording, _) in GeneratedFlowBuildTests.Recordings())
            yield return new Seed($"Build/{name}", recording.ToJson());
        if (Environment.GetEnvironmentVariable("QAAS_FUZZ_SEEDS") is { Length: > 0 } more && Directory.Exists(more))
            foreach (var path in Directory.GetFiles(more, "*.recording.json").Order(StringComparer.Ordinal))
                yield return new Seed($"More/{Path.GetFileName(path)[..^".recording.json".Length]}", File.ReadAllText(path));
    }

    /// <summary>The recordings the fuzz cut its failures down to, which the normal suite keeps, by name.</summary>
    public static IEnumerable<string> Regressions() =>
        Directory.GetFiles(Here(), "*.recording.json").Order(StringComparer.Ordinal)
            .Select(path => Path.GetFileName(path)[..^".recording.json".Length]);

    public static Recording Regression(string name) =>
        Recording.FromJson(File.ReadAllText(Path.Combine(Here(), $"{name}.recording.json")));

    // ── Mutants ──────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A recording changed from a seed: its JSON, what was changed, whether the flow is written with auto checks, and the
    /// flow's name, which may be one a person would give that names a type or member of the flow's code too. Each flow is
    /// in a namespace of its own, so no name of one is another's.
    /// </summary>
    public sealed record Mutant(int Number, string Seed, string Json, IReadOnlyList<string> Changes, bool AutoChecks)
    {
        public string ClassName { get; init; } = $"Fuzz{Number:D5}";

        public string Folder => $"M{Number:D5}";

        public string Namespace => $"{GeneratorFuzz.Namespace}.{Folder}";
    }

    // Flow names a person may give, which the recorder takes, that name what a flow's code uses or has; those it refuses,
    // as the types and members of the code, left out.
    private static readonly string[] ClassNames =
    [
        .. new[]
        {
        "ILocator", "SelectOptionValue", "CultureInfo", "Uri", "DateTime", "NotImplementedException", "IBrowserContext",
        "Configuration", "BaseUrl", "Unique", "RunAsync", "Item", "ItemIndex", "Context", "Page", "Dialog", "Flow", "Login",
        "Microsoft", "System", "QaaS", "Assertions", "Playwright", "Math", "String", "Object", "Exception", "Line", "Lines",
        "FileToUpload", "AcceptBrowserDialog", "OpenOrdersAsync", "Value", "Search", "Globalization", "StringComparison",
        "Flow2faLogin", "LoadAndValidateConfiguration", "Keyboard", "Mouse", "Name", "Exact", "Timeout", "Force",
        }.Where(name => !CSharp.Reserved.Contains(name)),
    ];

    /// <summary>The seed changed one to six times, the same for the same random state.</summary>
    public static Mutant Mutate(Seed seed, int number, Random random)
    {
        var root = JsonNode.Parse(seed.Json)!.AsObject();
        var changes = new List<string>();
        var times = 1 + (int)Math.Min(5, -Math.Log(1 - random.NextDouble()) * 1.5);
        for (var change = 0; change < times; change++) changes.Add(MutateOnce(root, random, number * 100 + change));
        var mutant = new Mutant(number, seed.Name, root.ToJsonString(Indented), changes, AutoChecks: random.Next(4) != 0);
        return random.Next(8) == 0 ? mutant with { ClassName = One(random, ClassNames) } : mutant;
    }

    private static readonly JsonSerializerOptions Indented = new(Recording.Json) { WriteIndented = true };

    private static string MutateOnce(JsonObject root, Random random, int stamp)
    {
        var actions = root["actions"] as JsonArray ?? [];
        root["actions"] = actions;
        JsonObject? Pick() => actions.Count == 0 ? null : actions[random.Next(actions.Count)] as JsonObject;
        int At(int extra = 0) => random.Next(actions.Count + extra);
        var id = $"fuzz-{stamp}";
        switch (random.Next(26))
        {
            case 0 when actions.Count > 0:
            {
                var at = At();
                actions.RemoveAt(at);
                return $"drop #{at}";
            }
            case 1 when Pick() is { } action:
            {
                var copy = action.DeepClone().AsObject();
                var sameId = random.Next(8) == 0;
                if (!sameId) copy["id"] = id;
                var at = At(1);
                actions.Insert(at, copy);
                return $"duplicate {action["id"]} to #{at}{(sameId ? " with its id" : "")}";
            }
            case 2 when actions.Count > 1:
            {
                var (from, to) = (At(), At());
                var moved = actions[from]!;
                actions.RemoveAt(from);
                actions.Insert(Math.Min(to, actions.Count), moved);
                return $"move #{from} to #{to}";
            }
            case 3 when actions.Count > 1:
            {
                var keep = random.Next(1, actions.Count);
                var head = random.Next(2) == 0;
                var kept = (head ? actions.Take(keep) : actions.Skip(actions.Count - keep)).Select(node => node!.DeepClone()).ToList();
                actions.Clear();
                foreach (var node in kept) actions.Add(node);
                return $"keep the {(head ? "first" : "last")} {keep}";
            }
            case 4 or 5 or 6 when Pick() is { } action:
                return $"{action["id"]}: {Blank(action, random)}";
            case 7 when Pick() is { } action:
                return $"{action["id"]}: {Odd1(action, random)}";
            case 8:
            {
                var at = At(1);
                actions.Insert(at, new JsonObject
                {
                    ["id"] = id, ["t"] = TimeAt(actions, at), ["kind"] = "navigate", ["page"] = PageNear(actions, at, random),
                    ["url"] = random.Next(6) == 0 ? null : One(random, Addresses), ["afterPause"] = random.Next(2) == 0 ? true : null,
                });
                return $"navigate at #{at}";
            }
            case 9:
            {
                var page = NewPage(actions);
                var at = At(1);
                actions.Insert(at, new JsonObject
                {
                    ["id"] = id, ["t"] = TimeAt(actions, at), ["kind"] = "newTab", ["page"] = page,
                    ["url"] = random.Next(5) == 0 ? null : One(random, Addresses), ["afterPause"] = random.Next(3) == 0 ? true : null,
                });
                var moved = MoveTo(actions, at + 1, page, random);
                return $"new tab {page} at #{at}, {moved} actions in it";
            }
            case 10 when Pick() is { } action:
            {
                var page = NewPage(actions);
                Effects(action).Add(new JsonObject { ["kind"] = "popup", ["page"] = random.Next(8) == 0 ? null : page, ["t"] = action["t"]?.GetValue<long>() ?? 0 });
                var moved = MoveTo(actions, actions.IndexOf(action) + 1, page, random);
                return $"{action["id"]} opens pop-up {page}, {moved} actions in it";
            }
            case 11 when actions.OfType<JsonObject>().Where(action => action["page"]?.GetValue<string>() is { } page && page != "page").ToList() is [_, ..] others:
            {
                // The pop-up or tab closed, or what opened it was deleted: its actions go back to the page, or stay.
                var action = others[random.Next(others.Count)];
                if (random.Next(2) == 0)
                {
                    action["page"] = "page";
                    return $"{action["id"]} back on page";
                }

                var page = action["page"]!.GetValue<string>();
                foreach (var opener in actions.OfType<JsonObject>())
                {
                    if (opener["kind"]?.GetValue<string>() == "newTab" && opener["page"]?.GetValue<string>() == page) opener["kind"] = "click";
                    if (opener["effects"] is JsonArray effects)
                        foreach (var popup in effects.OfType<JsonObject>().Where(effect => effect["page"]?.GetValue<string>() == page).ToList())
                            effects.Remove(popup);
                }

                return $"nothing opens {page}";
            }
            case 12 when Pick() is { } action:
            {
                Effects(action).Add(new JsonObject
                {
                    ["kind"] = "urlChanged", ["url"] = random.Next(6) == 0 ? null : One(random, Addresses),
                    ["page"] = random.Next(6) == 0 ? null : action["page"]?.DeepClone(), ["t"] = action["t"]?.GetValue<long>() ?? 0,
                });
                return $"{action["id"]} changes the address";
            }
            case 13 when Pick() is { } action:
                return $"{action["id"]}: {Edit(action, actions, random)}";
            case 14:
            {
                var at = At(1);
                actions.Insert(at, Check(actions, at, id, random));
                return $"check at #{at}";
            }
            case 15 when actions.Count > 0:
                return Loop(actions, random, id);
            case 16 when actions.OfType<JsonObject>().Where(action => action["kind"]?.GetValue<string>() == "fill").ToList() is [_, ..] fills:
            {
                var fill = fills[random.Next(fills.Count)];
                var target = fill["target"] as JsonObject ?? [];
                fill["target"] = target;
                switch (random.Next(4))
                {
                    case 0: target["type"] = "password"; break;
                    case 1: target["name"] = One(random, ["password", "apiToken", "client_secret", "Password"]); break;
                    case 2: target["label"] = "API key"; break;
                    default:
                        var edit = fill["edit"] as JsonObject ?? [];
                        fill["edit"] = edit;
                        edit["values"] = new JsonObject { ["value"] = new JsonObject { ["name"] = "ApiToken" } };
                        break;
                }

                // A value no file may hold, typed, then maybe checked, once typed while paused, so only its check is left.
                var secret = $"{SecretMark}{stamp}";
                (fill["value"], fill["raw"]) = (secret, secret);
                var at = actions.IndexOf(fill);
                if (random.Next(2) == 0)
                    actions.Insert(at + 1, new JsonObject
                    {
                        ["id"] = $"{id}-check", ["t"] = (fill["t"]?.GetValue<long>() ?? 0) + 1, ["kind"] = "expect", ["check"] = "value",
                        ["value"] = secret, ["page"] = fill["page"]?.DeepClone(), ["target"] = target.DeepClone(),
                    });
                if (random.Next(4) == 0) actions.Remove(fill);
                return $"{fill["id"]} is a secret";
            }
            case 17:
                root["startUrl"] = One(random, Addresses) is { Length: > 0 } address || random.Next(2) == 0 ? One(random, Addresses) : "";
                return $"start at {root["startUrl"]}";
            case 18 when Pick() is { } action:
                action["kind"] = random.Next(6) == 0 ? One(random, ["hover", "", "scroll", "Click"]) : One(random, Kinds);
                return $"{action["id"]} is a {action["kind"]}";
            case 19 when Pick() is { } action:
                action["widget"] = random.Next(5) == 0 ? null : One(random, Widgets);
                return $"{action["id"]} widget {action["widget"]}";
            case 20 when Pick() is { } action:
                Effects(action).Add(Effect(actions, action, random));
                return $"{action["id"]} effect {Effects(action)[^1]!["kind"]}";
            case 21 when Pick() is { } action:
            {
                var titles = Titles(actions).Append("Fuzz dialog").Append(One(random, Odd)).ToList();
                if (random.Next(4) == 0)
                    action["dialog"] = null;
                else
                    action["dialog"] = Dialog(One(random, [.. titles]), random);
                action["openDialogs"] = random.Next(4) == 0 ? null
                    : new JsonArray([.. titles.Where(_ => random.Next(3) == 0).Select(title => (JsonNode?)title)]);
                return $"{action["id"]} in dialog {action["dialog"]?["title"]}";
            }
            case 22 when Pick() is { } action:
            {
                var target = action["target"] as JsonObject ?? [];
                action["target"] = target;
                switch (random.Next(3))
                {
                    case 0:
                        target["row"] = new JsonObject
                        {
                            ["key"] = random.Next(5) == 0 ? null : One(random, ["TCK-1041", "Ada", "", "12", "a\"b"]),
                            ["header"] = random.Next(4) == 0 ? null : One(random, ["Ticket", "Name", "", "__"]),
                        };
                        Candidates(target).Add(new JsonObject
                        {
                            ["kind"] = "row", ["count"] = 1, ["matches"] = true,
                            ["parts"] = new JsonArray(
                                new JsonObject { ["m"] = "role", ["role"] = "row" },
                                new JsonObject { ["m"] = "filter", ["hasText"] = target["row"]!["key"]?.DeepClone() },
                                new JsonObject { ["m"] = "role", ["role"] = target["role"]?.DeepClone() ?? "button", ["name"] = target["text"]?.DeepClone() }),
                        });
                        return $"{action["id"]} in a row";
                    case 1:
                        target["chosen"] = One(random, [-1, 0, 1, 99]);
                        return $"{action["id"]} chosen {target["chosen"]}";
                    default:
                        target["role"] = One(random, ["button", "checkbox", "radio", "switch", "combobox", "option", "bogus", "row", "dialog"]);
                        return $"{action["id"]} role {target["role"]}";
                }
            }
            case 23 when actions.Count > 0:
                return ApplySuggestions(root, random);
            // A page whose ids, names, labels and texts have no letter a C# identifier can use, or invisible marks.
            case 24 when Pick() is { } action:
            {
                var texts = Walk(action).Where(found => found.Key is "id" or "name" or "testId" or "label" or "text" or "placeholder"
                                                            or "groupLabel" or "section" or "value" or "title" or "heading" or "hasText"
                                                        && found.Node is JsonValue value && value.TryGetValue<string>(out _)
                                                        && found.Parent != action).ToList();
                if (texts.Count == 0) return "no text to replace";
                var (parent, key, _) = texts[random.Next(texts.Count)];
                var translated = One(random, ["___", "_1_", "123", "__-__", "-", "\u200e\u200f", "\u200bx\u200b", "\u2066x\u2069", "😀", "Équipe"]);
                if (parent is JsonObject owner) owner[(string)key] = translated;
                else ((JsonArray)parent)[(int)key] = translated;
                return $"{action["id"]}: {key} = {translated}";
            }
            case 25 when Pick() is { } action:
            {
                action["t"] = random.Next(3) switch { 0 => 0, 1 => (Pick()?["t"]?.GetValue<long>() ?? 0), _ => (action["t"]?.GetValue<long>() ?? 0) + 3_600_000 };
                return $"{action["id"]} at {action["t"]}";
            }
        }

        root["autoChecks"] = random.Next(3) switch { 0 => null, 1 => true, _ => false };
        root["after"] = random.Next(3) == 0 ? One(random, ["LoginFlow", "Login", "class", "2fa"]) : null;
        root["notRecorded"] = random.Next(3) == 0 ? new JsonArray(One(random, Odd)) : null;
        return "the recording's own fields";
    }

    // One field of the action, at any depth, null, empty or gone; a named one of the fields the generator reads more often.
    private static string Blank(JsonObject action, Random random)
    {
        string[] named =
        [
            "target", "target.candidates", "target.candidates[*].parts", "target.candidates[*]", "url", "page", "dialog", "dialog.title",
            "openDialogs", "value", "raw", "effects", "effects[*].dialog", "effects[*].url", "effects[*].text", "effects[*].page",
            "effects[*].value", "effects[*].dialog.title", "target.row", "target.row.key", "target.row.header", "edit", "edit.values",
            "edit.dropped", "edit.step", "field", "option", "opener", "key", "files", "to", "scrolled", "scrolled.list", "hoverOpener",
            "group", "check", "widget", "target.text", "target.label", "target.name", "target.id", "target.tag", "target.role",
            "target.candidates[*].csharp", "target.candidates[*].text", "target.candidates[*].value", "target.candidates[*].kind",
            "target.candidates[*].parts[*].m", "target.candidates[*].parts[*].role", "target.candidates[*].parts[*].name",
            "target.candidates[*].parts[*].css", "target.candidates[*].parts[*].hasText", "target.candidates[*].parts[*].text",
            "field.candidates", "option.candidates", "group.candidates", "kind", "id", "equivalents", "hoverOpener.candidates",
        ];
        var found = random.Next(3) == 0 ? AnyPath(action, random) : Find(action, One(random, named), random);
        if (found is not var (parent, key, node)) return "nothing to blank";
        var how = random.Next(4);
        JsonNode? value = node switch
        {
            JsonValue text when text.TryGetValue<string>(out _) && how == 1 => "",
            JsonArray when how == 1 => new JsonArray(),
            JsonObject when how == 1 => new JsonObject(),
            JsonValue other when !other.TryGetValue<string>(out _) => null,
            _ => null,
        };
        // A number or a flag is never null in a recording: it goes, and reads as its default.
        var remove = how >= 2 || node is JsonValue number && !number.TryGetValue<string>(out _);
        switch (parent)
        {
            case JsonObject owner when remove:
                owner.Remove((string)key);
                return $"{key} gone";
            case JsonObject owner:
                owner[(string)key] = value;
                return $"{key} {(value is null ? "null" : "empty")}";
            case JsonArray list when remove:
                list.RemoveAt((int)key);
                return $"item {key} gone";
            case JsonArray list:
                list[(int)key] = value;
                return $"item {key} {(value is null ? "null" : "empty")}";
        }

        return "nothing to blank";
    }

    // A text of the action, at any depth, odd.
    private static string Odd1(JsonObject action, Random random)
    {
        var texts = Walk(action).Where(found => found.Node is JsonValue value && value.TryGetValue<string>(out _)
                                                                                && found.Key is not "kind" and not "m" and not "id").ToList();
        if (texts.Count == 0) return "no text";
        var (parent, key, _) = texts[random.Next(texts.Count)];
        var odd = One(random, Odd);
        if (parent is JsonObject owner) owner[(string)key] = odd;
        else ((JsonArray)parent)[(int)key] = odd;
        return $"{key} = {JsonSerializer.Serialize(odd)}";
    }

    // The person's edits in the panel: a step's name, auto checks dropped, a setting renamed, its default, a stamp, a date.
    private static string Edit(JsonObject action, JsonArray actions, Random random)
    {
        var edit = action["edit"] as JsonObject ?? [];
        action["edit"] = edit;
        switch (random.Next(3))
        {
            case 0:
                edit["step"] = One(random, ["SaveItAsync", "Save it", "123", "class", "", "Run", "RunAsync", "Fuzz00001", "OpenAsync", "Async",
                    "Configuration", "BaseUrl", "Équipe", "AcceptBrowserDialog", "FileToUpload", .. Odd.Take(8)]);
                return $"step named {edit["step"]}";
            case 1:
                edit["dropped"] = new JsonArray([.. new[] { "url", "opened", "shows", "selected", "alert:Saved", $"closed:{Titles(actions).FirstOrDefault()}", "", "bogus" }
                    .Where(_ => random.Next(2) == 0).Select(key => (JsonNode?)key)]);
                return $"dropped {edit["dropped"]!.ToJsonString()}";
            default:
            {
                var values = edit["values"] as JsonObject ?? [];
                edit["values"] = values;
                var role = One(random, ["value", "value", "search", "option", "shown", "row", "bogus"]);
                values[role] = new JsonObject
                {
                    ["name"] = random.Next(4) == 0 ? null : One(random, ["Name", "My value", "123abc", "", "class", "Configuration", "BaseUrl", "Value",
                        "RunAsync", "Equals", "Item", "Fuzz00001Config", "Email", "Password", "ApiKey", .. Odd.Take(6)]),
                    ["value"] = random.Next(4) == 0 ? null : One(random, ["", "3", "-2", "abc", "1.5", "99999999999", "0", "-0", .. Odd.Take(6)]),
                    ["unique"] = random.Next(3) == 0 ? true : null,
                    ["dateFormat"] = random.Next(3) == 0 ? One(random, ["dd/MM/yyyy", "yyyy-MM-dd", "", "\"", "{0}", "%"]) : null,
                };
                return $"edits {role}: {values[role]!.ToJsonString()}";
            }
        }
    }

    // A check the person chose, of an element another action acted on, of its group or of the page.
    private static JsonObject Check(JsonArray actions, int at, string id, Random random)
    {
        var other = actions.Count == 0 ? null : actions[random.Next(actions.Count)] as JsonObject;
        var kind = One(random, CheckKinds);
        var check = new JsonObject
        {
            ["id"] = id, ["t"] = TimeAt(actions, at), ["kind"] = "expect", ["page"] = PageNear(actions, at, random),
            ["url"] = other?["url"]?.DeepClone(), ["check"] = random.Next(10) == 0 ? null : kind,
            ["value"] = random.Next(4) == 0 ? null : One(random, [.. Odd.Take(12), "3", "https://app.test/start/orders/5", "Saved"]),
            ["target"] = random.Next(6) == 0 ? null : (other?["target"] ?? other?["field"])?.DeepClone(),
            ["dialog"] = random.Next(2) == 0 ? other?["dialog"]?.DeepClone() : null,
        };
        if (kind is "count" or "row" || random.Next(5) == 0)
            check["group"] = random.Next(5) == 0 ? null : new JsonObject
            {
                ["tag"] = "tr",
                ["candidates"] = new JsonArray(new JsonObject
                {
                    ["kind"] = "row", ["count"] = random.Next(-1, 4), ["matches"] = random.Next(3) != 0,
                    ["parts"] = new JsonArray(
                        new JsonObject { ["m"] = "role", ["role"] = "row" },
                        new JsonObject { ["m"] = "filter", ["hasText"] = random.Next(5) == 0 ? null : One(random, Odd) }),
                }),
            };
        return check;
    }

    // A round of up to three actions done again two to four times, each round typing, choosing or acting on its own.
    private static string Loop(JsonArray actions, Random random, string id)
    {
        var start = random.Next(actions.Count);
        var length = Math.Min(random.Next(1, 4), actions.Count - start);
        var round = actions.Skip(start).Take(length).Select(node => node!.AsObject()).ToList();
        var repeats = random.Next(2, 5);
        var at = start + length;
        for (var repeat = 1; repeat <= repeats; repeat++)
            foreach (var (action, index) in round.Select((action, index) => (action, index)))
            {
                var copy = action.DeepClone().AsObject();
                copy["id"] = $"{id}-{repeat}-{index}";
                copy["t"] = (action["t"]?.GetValue<long>() ?? 0) + repeat * 10_000 + index;
                if (copy["value"]?.GetValue<string>() is { } value && random.Next(4) != 0) copy["value"] = $"{value} {repeat + 1}";
                if (copy["target"]?["row"] is JsonObject row && row["key"]?.GetValue<string>() is { } key)
                {
                    row["key"] = $"{key}-{repeat}";
                    foreach (var part in Walk(copy["target"]!).Where(found => found.Key is "hasText" && found.Node?.ToString() == key))
                        ((JsonObject)part.Parent)["hasText"] = $"{key}-{repeat}";
                }

                if (random.Next(10) == 0) copy["kind"] = One(random, Kinds);
                actions.Insert(at++, copy);
            }

        return $"#{start}..#{start + length - 1} again {repeats} times";
    }

    // What a page does after an action: any kind, with what it holds or without.
    private static JsonObject Effect(JsonArray actions, JsonObject action, Random random)
    {
        var kind = One(random, EffectKinds);
        var effect = new JsonObject { ["kind"] = kind, ["t"] = (action["t"]?.GetValue<long>() ?? 0) + random.Next(0, 1000) };
        switch (kind)
        {
            case "dialogOpened" or "dialogClosed":
                effect["dialog"] = random.Next(6) == 0 ? null : Dialog(One(random, [.. Titles(actions), "Fuzz dialog", "", One(random, Odd)]), random);
                break;
            case "alert":
                effect["text"] = random.Next(6) == 0 ? null : One(random, [.. Odd, "Ticket deleted", "Order 5521 saved", "Loading..."]);
                effect["alertCount"] = random.Next(0, 3);
                break;
            case "settled":
                effect["value"] = random.Next(4) == 0 ? null : One(random, Odd);
                effect["raw"] = random.Next(2) == 0 ? null : One(random, Odd);
                effect["multi"] = random.Next(3) == 0 ? true : null;
                break;
            case "busy":
                effect["value"] = One(random, ["progressbar", "ariaBusy", "bogus"]);
                break;
            case "urlChanged":
                effect["url"] = random.Next(6) == 0 ? null : One(random, Addresses);
                effect["page"] = action["page"]?.DeepClone();
                break;
            case "popup":
                effect["page"] = NewPage(actions);
                break;
            case "jsDialog":
                effect["text"] = random.Next(4) == 0 ? null : One(random, Odd);
                effect["page"] = random.Next(5) == 0 ? null : action["page"]?.DeepClone();
                break;
        }

        return effect;
    }

    private static JsonObject Dialog(string title, Random random) => new()
    {
        ["title"] = title, ["raw"] = random.Next(3) == 0 ? null : title, ["titleMatches"] = random.Next(3) != 0,
        ["prefix"] = random.Next(4) == 0 ? title.Split(' ')[0] : null, ["prefixMatches"] = random.Next(4) == 0,
        ["rawMatches"] = random.Next(4) == 0, ["heading"] = random.Next(4) == 0 ? title : null, ["headingMatches"] = random.Next(4) == 0,
        ["unnamedMatches"] = random.Next(2) == 0,
    };

    // The person applied some of the recorder's suggestions: the actions they leave out, with what led to them.
    private static string ApplySuggestions(JsonObject root, Random random)
    {
        Recording recording;
        FlowPlan plan;
        try
        {
            recording = Recording.FromJson(root.ToJsonString());
            plan = FlowPlanner.Plan(recording, "Suggested");
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return "no suggestion to apply";
        }

        var applied = plan.Suggestions.Where(_ => random.Next(2) == 0).ToList();
        var sources = FlowPlanner.Sources(recording, [.. plan.Steps.SelectMany(step => step.Actions)]);
        var drop = applied.SelectMany(suggestion => suggestion.Drop).SelectMany(id => sources.GetValueOrDefault(id) ?? [id]).ToHashSet();
        var actions = (JsonArray)root["actions"]!;
        foreach (var action in actions.OfType<JsonObject>().Where(action => drop.Contains(action["id"]?.ToString() ?? "")).ToList())
            actions.Remove(action);
        return $"applied {applied.Count} suggestions, {drop.Count} actions left out";
    }

    private static int MoveTo(JsonArray actions, int from, string page, Random random)
    {
        var count = Math.Min(random.Next(0, 4), actions.Count - from);
        for (var index = from; index < from + count; index++)
            if (actions[index] is JsonObject action) action["page"] = page;
        return count;
    }

    private static string NewPage(JsonArray actions)
    {
        var pages = Walk(actions).Where(found => found.Key is "page").Select(found => found.Node?.ToString()).OfType<string>().ToHashSet();
        var number = 1;
        while (pages.Contains($"page{number}")) number++;
        return $"page{number}";
    }

    private static JsonNode? PageNear(JsonArray actions, int at, Random random) =>
        random.Next(8) == 0 ? null
        : actions.Count == 0 ? "page"
        : actions[Math.Min(at, actions.Count - 1)]?["page"]?.DeepClone() ?? "page";

    private static long TimeAt(JsonArray actions, int at) =>
        actions.Count == 0 ? 1_000_000 : (actions[Math.Clamp(at - 1, 0, actions.Count - 1)]?["t"]?.GetValue<long>() ?? 1_000_000) + 1;

    private static IEnumerable<string> Titles(JsonArray actions) =>
        Walk(actions).Where(found => found.Key is "title").Select(found => found.Node?.ToString()).OfType<string>().Distinct();

    private static JsonArray Effects(JsonObject action)
    {
        if (action["effects"] is JsonArray effects) return effects;
        effects = [];
        action["effects"] = effects;
        return effects;
    }

    private static JsonArray Candidates(JsonObject target)
    {
        if (target["candidates"] is JsonArray candidates) return candidates;
        candidates = [];
        target["candidates"] = candidates;
        return candidates;
    }

    private static T One<T>(Random random, IReadOnlyList<T> choices) => choices[random.Next(choices.Count)];

    // Every property and item under the node, with where it is.
    private static IEnumerable<(JsonNode Parent, object Key, JsonNode? Node)> Walk(JsonNode node)
    {
        switch (node)
        {
            case JsonObject owner:
                foreach (var (key, value) in owner.ToList())
                {
                    yield return (owner, key, value);
                    if (value is not null)
                        foreach (var inner in Walk(value)) yield return inner;
                }

                break;
            case JsonArray list:
                for (var index = 0; index < list.Count; index++)
                {
                    yield return (list, index, list[index]);
                    if (list[index] is { } item)
                        foreach (var inner in Walk(item)) yield return inner;
                }

                break;
        }
    }

    private static (JsonNode Parent, object Key, JsonNode? Node)? AnyPath(JsonObject action, Random random)
    {
        var all = Walk(action).ToList();
        return all.Count == 0 ? null : all[random.Next(all.Count)];
    }

    // The node at a path such as target.candidates[*].parts, [*] any item; null when the action has none there.
    private static (JsonNode Parent, object Key, JsonNode? Node)? Find(JsonObject action, string path, Random random)
    {
        JsonNode? node = action;
        (JsonNode Parent, object Key, JsonNode? Node)? found = null;
        foreach (var segment in path.Split('.'))
        {
            var name = segment.EndsWith("[*]", StringComparison.Ordinal) ? segment[..^3] : segment;
            if (node is not JsonObject owner || !owner.ContainsKey(name)) return null;
            found = (owner, name, owner[name]);
            node = owner[name];
            if (!segment.EndsWith("[*]", StringComparison.Ordinal)) continue;
            if (node is not JsonArray { Count: > 0 } list) return null;
            var index = random.Next(list.Count);
            found = (list, index, list[index]);
            node = list[index];
        }

        return found;
    }

    // ── Checks without a build ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>What the generator did with a recording: the flow, or why it is wrong, as one line that tells failures apart.</summary>
    public sealed record Outcome(GeneratedFlow? Flow, string? Problem, string? Detail = null);

    /// <summary>
    /// The flow of the recording, and what is wrong: it throws; an action of the plan is neither written nor said to check
    /// by hand; a line of the action, step, statement or check-by-hand maps is not in the code, or a statement is not
    /// inside its actions' lines; the test build cannot instrument it.
    /// </summary>
    public static Outcome Check(Recording recording, string className, bool autoChecks, string namespaceName = Namespace)
    {
        var secrets = Secrets(recording);
        // As autosave keeps it, before anything reads it.
        var kept = Autosave.WithoutSecrets(recording).ToJson();
        FlowPlan plan;
        GeneratedFlow flow;
        try
        {
            plan = FlowPlanner.Plan(recording, className);
            flow = FlowWriter.Write(plan, namespaceName, autoChecks);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return new Outcome(null, $"throws {failure.GetType().Name} in {Where(failure)}", failure.ToString());
        }

        // An action the writer had to leave out, as it threw, is the writer's bug still, which the fuzz is to find.
        if (flow.Checks.FirstOrDefault(check => check.Message.StartsWith(FlowWriter.NotWritten, StringComparison.Ordinal)) is { } left)
            return new Outcome(flow, $"leaves out an action it threw on: {Generalised(left.Message.Split(':')[0])}", left.Message);
        var lines = flow.Code.Split('\n');
        var count = lines.Length - (lines[^1].Length == 0 ? 1 : 0);
        bool Valid(LineSpan span) => span.First >= 1 && span.First <= span.Last && span.Last <= count;
        var problems = new List<string>();
        foreach (var (id, span) in flow.ActionLines.Where(pair => !Valid(pair.Value)))
            problems.Add($"lines of action {id} {span} outside 1..{count}");
        foreach (var (name, span) in flow.StepLines)
            if (!Valid(span)) problems.Add($"lines of step {name} {span} outside 1..{count}");
            else if (!lines[span.First - 1].Contains($" {name}(", StringComparison.Ordinal)) problems.Add($"step {name} does not start at line {span.First}");
        foreach (var statement in flow.Statements)
        {
            if (!Valid(statement.Lines)) problems.Add($"statement {statement.Lines} outside 1..{count}");
            if (statement.Actions.Count == 0) problems.Add($"statement {statement.Lines} of no action");
            foreach (var id in statement.Actions)
                if (!flow.ActionLines.TryGetValue(id, out var span) || span.First > statement.Lines.First || span.Last < statement.Lines.Last)
                    problems.Add($"statement {statement.Lines} outside the lines of its action {id}");
        }

        foreach (var check in flow.Checks.Where(check => check.Line is not null && (check.Line < 1 || check.Line > count)))
            problems.Add($"check by hand at line {check.Line} outside 1..{count}");
        if (flow.Checks.Any(check => string.IsNullOrWhiteSpace(check.Message))) problems.Add("check by hand that says nothing");
        foreach (var action in plan.Steps.SelectMany(step => step.Actions).Where(action => !flow.ActionLines.ContainsKey(action.Source.Id)))
            if (!flow.Checks.Any(check => check.Message.Contains($"'{action.Kind}'", StringComparison.Ordinal)))
                problems.Add($"action {action.Source.Id} ({action.Kind}) neither written nor to check by hand");
        try
        {
            FlowBuild.Instrument(flow);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            problems.Add($"the test build cannot instrument it: {failure.GetType().Name} in {Where(failure)}");
        }

        // No file, nor what the panel and the console show, holds a secret: the code, the YAML, what to check by hand, the
        // checks and suggestions, and the recording autosave keeps.
        var shown = new[] { flow.Code, flow.Yaml, kept }
            .Concat(flow.Checks.Select(check => check.Message)).Concat(flow.Suggestions.Select(suggestion => suggestion.Text))
            .Concat(flow.Expects.Values.SelectMany(expects => expects).Select(expect => expect.Text));
        if (secrets.FirstOrDefault(secret => shown.Any(text => text.Contains(secret, StringComparison.Ordinal))) is { } leaked)
            problems.Insert(0, $"a secret is in what the flow writes or shows: {leaked}");
        return problems.Count == 0 ? new Outcome(flow, null) : new Outcome(flow, Generalised(problems[0]), string.Join("\n", problems));
    }

    /// <summary>What marks a value the fuzz plants as a secret, which nothing written or shown may hold.</summary>
    public const string SecretMark = "qaas-secret-";

    // The secrets planted that only actions holding a secret hold, as a copy of one into a field of no secret holds its
    // value as text, rightly.
    private static List<string> Secrets(Recording recording)
    {
        string[] Texts(CapturedAction action) => [action.Value ?? "", action.Raw ?? "", action.Edit?.Values?.GetValueOrDefault("value")?.Value ?? ""];
        var open = recording.Actions.Where(action => !SecretField.Holds(action)).SelectMany(Texts).ToHashSet();
        return [.. recording.Actions.Where(SecretField.Holds).SelectMany(Texts).Where(text => text.StartsWith(SecretMark, StringComparison.Ordinal))
            .Where(text => !open.Any(other => other.Contains(text, StringComparison.Ordinal))).Distinct()];
    }

    // The first frame of the recorder's own code, without its line: where it threw, the same for the same bug.
    private static string Where(Exception failure) =>
        new StackTrace(failure, fNeedFileInfo: false).GetFrames().Select(frame => frame.GetMethod())
            .Where(method => method?.DeclaringType?.Namespace?.StartsWith("QaaS.Playwright.Recorder", StringComparison.Ordinal) == true)
            .Select(method => $"{Outer(method!.DeclaringType!).Name}.{Readable(method!.Name)}").FirstOrDefault() ?? "?";

    private static Type Outer(Type type) => type.DeclaringType is { } outer ? Outer(outer) : type;

    // "<WriteLoop>b__12_0" is in WriteLoop.
    private static string Readable(string method) => AngleName().Match(method) is { Success: true } inner ? inner.Groups[1].Value : method;

    // The problem without what changes from mutant to mutant: ids, numbers, names.
    private static string Generalised(string problem) => Quoted().Replace(Numbers().Replace(Ids().Replace(problem, "<id>"), "N"), "'…'");

    // ── The build ────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the flows in a project of their own, as strict as a team's (<see cref="FlowCompiler"/>), and their code as
    /// the panel's test instruments it in another; binds the YAML of each that built to its settings. Returns what is
    /// wrong with each, by its number: its first build error, or how its YAML binds otherwise than its defaults.
    /// </summary>
    public static async Task<Dictionary<int, (string Problem, string Detail)>> BuildAsync(string folder,
        IReadOnlyList<(Mutant Mutant, GeneratedFlow Flow)> flows, int batch)
    {
        var problems = new Dictionary<int, (string, string)>();
        var strict = Path.Combine(folder, $"strict{batch}");
        var instrumented = Path.Combine(folder, $"instrumented{batch}");
        Directory.CreateDirectory(strict);
        Directory.CreateDirectory(instrumented);
        var assemblyName = $"FuzzFlows{batch}";
        await FlowCompiler.CreateProjectAsync(strict, assemblyName);
        // The project names its flows' namespace after its root namespace, which the fuzz names its own.
        var project = Path.Combine(strict, $"{assemblyName}.csproj");
        await File.WriteAllTextAsync(project, (await File.ReadAllTextAsync(project)).Replace("<RootNamespace>GeneratedFlows</RootNamespace>",
            "<RootNamespace>FuzzFlows</RootNamespace>", StringComparison.Ordinal));
        await CreateInstrumentedProjectAsync(instrumented);
        string FileOf(Mutant mutant, string root) => Path.Combine(Directory.CreateDirectory(Path.Combine(root, mutant.Folder)).FullName, $"{mutant.ClassName}.cs");
        foreach (var (mutant, flow) in flows)
        {
            await File.WriteAllTextAsync(FileOf(mutant, FlowCompiler.FlowsFolder(strict)), flow.Code);
            await File.WriteAllTextAsync(FileOf(mutant, instrumented), FlowBuild.Instrument(flow));
        }

        var byClass = flows.ToDictionary(flow => flow.Mutant.Folder, flow => flow.Mutant);
        var testBuild = BuildLogAsync(instrumented, Path.Combine(instrumented, "out"));
        // A flow that does not build keeps the others from it: built again without it, until every other builds.
        string? assembly = null;
        for (var round = 0; round < 4 && assembly is null; round++)
        {
            var (built, log) = await BuildLogAsync(strict, Path.Combine(strict, "out"));
            var errors = Errors(log).Where(error => byClass.ContainsKey(error.Class)).ToList();
            foreach (var error in errors.GroupBy(error => error.Class))
            {
                problems.TryAdd(byClass[error.Key].Number, ($"does not build: {Generalised(error.First().Code)}", string.Join("\n", error.Select(each => each.Text).Distinct())));
                File.Delete(FileOf(byClass[error.Key], FlowCompiler.FlowsFolder(strict)));
            }

            if (built) assembly = Path.Combine(strict, "out", $"{assemblyName}.dll");
            else if (errors.Count == 0) throw new InvalidOperationException($"The fuzz project did not build, for no flow's error:\n{log}");
        }

        var (_, testLog) = await testBuild;
        foreach (var error in Errors(testLog).Where(error => byClass.ContainsKey(error.Class)).GroupBy(error => error.Class))
            problems.TryAdd(byClass[error.Key].Number, ($"its test build does not build: {Generalised(error.First().Code)}",
                string.Join("\n", error.Select(each => each.Text).Distinct())));

        if (assembly is null) return problems;
        var types = FlowAssemblyContext.Load(assembly).GetTypes().Where(type => !type.IsNested).ToDictionary(type => type.FullName!);
        foreach (var (mutant, flow) in flows.Where(flow => !problems.ContainsKey(flow.Mutant.Number)))
            if (Binds(types[$"{mutant.Namespace}.{mutant.ClassName}"], flow, Path.Combine(folder, mutant.Folder)) is { } wrong)
                problems[mutant.Number] = ("its YAML does not bind to its settings", wrong);
        return problems;
    }

    /// <summary>
    /// Null when the flow's values, as its test run reads them, bind to its record's defaults, the secrets aside, which
    /// its YAML reads from the environment; else how they bind.
    /// </summary>
    public static string? Binds(Type type, GeneratedFlow flow, string folder)
    {
        var path = Path.Combine(Directory.CreateDirectory(folder).FullName, $"{type.Name}.flow.yaml");
        File.WriteAllText(path, flow.Values);
        var bound = (IPlaywrightFlow)Activator.CreateInstance(type)!;
        var problems = bound.LoadAndValidateConfiguration(LoginSettings.Read(path, type.Name).GetSection(type.Name));
        if (problems is { Count: > 0 }) return string.Join("; ", problems.Select(problem => problem.ErrorMessage));
        var secrets = flow.Secrets.Select(secret => secret.Name).ToHashSet();
        string Json(object? configuration)
        {
            var node = JsonSerializer.SerializeToNode(configuration)!.AsObject();
            foreach (var secret in secrets) node.Remove(secret);
            return node.ToJsonString();
        }

        var (got, expected) = (Json(type.GetProperty("Configuration")!.GetValue(bound)),
            Json(type.GetProperty("Configuration")!.GetValue(Activator.CreateInstance(type))));
        return got == expected ? null : $"binds {got}, its defaults are {expected}";
    }

    // The test run's project: the flows as the panel instruments them, compiled as FlowBuild compiles them.
    private static async Task CreateInstrumentedProjectAsync(string folder)
    {
        string[] references =
        [
            "QaaS.Playwright.dll", "Microsoft.Playwright.dll", "QaaS.Framework.SDK.dll", "Microsoft.Extensions.Configuration.Abstractions.dll",
            "Microsoft.Extensions.Logging.Abstractions.dll", "Microsoft.Bcl.AsyncInterfaces.dll",
        ];
        await File.WriteAllTextAsync(Path.Combine(folder, "FuzzTestRun.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <NoWarn>$(NoWarn);CS1591</NoWarn>
                <MSBuildWarningsAsMessages>MSB3277</MSBuildWarningsAsMessages>
              </PropertyGroup>
              <ItemGroup>
            {string.Join("\n", references.Select(name => $"""    <Reference Include="{Path.Combine(AppContext.BaseDirectory, name)}" Private="false" />"""))}
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(folder, "Probe.cs"), """
            namespace QaasRecorderTestRun;

            public static class Probe
            {
                public static System.Threading.Tasks.Task At(int statement) => System.Threading.Tasks.Task.CompletedTask;
            }
            """);
    }

    private static async Task<(bool Built, string Log)> BuildLogAsync(string project, string output)
    {
        var build = new ProcessStartInfo("dotnet", ["build", "--nologo", "-v", "q", "-clp:NoSummary", "-o", output, project])
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            Environment = { ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["DOTNET_NOLOGO"] = "1" },
        };
        using var process = Process.Start(build)!;
        var output1 = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode == 0, await output1 + await errors);
    }

    // "/tmp/.../Flows/Fuzz00042.cs(12,5): error CS1002: ; expected [/tmp/.../x.csproj]": the flow's class, the error's code.
    private static IEnumerable<(string Class, string Code, string Text)> Errors(string log) =>
        BuildError().Matches(log).Select(match => (match.Groups["class"].Value, $"{match.Groups["code"].Value}: {match.Groups["message"].Value.Trim()}",
            $"line {match.Groups["line"].Value}: {match.Groups["code"].Value}: {match.Groups["message"].Value.Trim()}"));

    // ── Cutting a failure down ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The smallest recording that still fails as <paramref name="fails"/> tells: fewer actions (ddmin), then each
    /// property and item of what is left removed while it still fails.
    /// </summary>
    public static async Task<string> MinimiseAsync(string json, Func<string, Task<bool>> fails, int budget = 400)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        var tries = 0;
        async Task<bool> Still(JsonObject candidate) => ++tries <= budget && await fails(candidate.ToJsonString());

        // Fewer actions: chunks of them removed, in halves, quarters, ... down to one.
        var actions = ((JsonArray)root["actions"]!).Select(node => node!.DeepClone()).ToList();
        for (var chunk = Math.Max(1, actions.Count / 2); chunk >= 1 && tries < budget; chunk /= 2)
            for (var start = 0; start < actions.Count && tries < budget;)
            {
                var kept = actions.Take(start).Concat(actions.Skip(start + chunk)).ToList();
                var candidate = With(root, kept);
                if (kept.Count < actions.Count && await Still(candidate)) actions = kept;
                else start += chunk;
            }

        root = With(root, actions);
        // Then each property and item, a whole one before what is in it, while it still fails.
        List<(List<object> Path, object Key)> Places(JsonObject of) =>
        [
            .. Walk(of).Where(place => !(ReferenceEquals(place.Parent, of) && place.Key is "actions" or "startUrl"))
                .Select(place => (PathOf(place.Parent), place.Key)),
        ];
        var places = Places(root);
        for (var index = 0; index < places.Count && tries < budget;)
        {
            var candidate = root.DeepClone().AsObject();
            if (Remove(At(candidate, places[index].Path), places[index].Key) && await Still(candidate))
            {
                root = candidate;
                places = Places(root);
            }
            else index++;
        }

        return root.ToJsonString(Indented);
    }

    private static JsonObject With(JsonObject root, IEnumerable<JsonNode> actions)
    {
        var copy = root.DeepClone().AsObject();
        copy["actions"] = new JsonArray([.. actions.Select(action => action.DeepClone())]);
        return copy;
    }

    private static bool Remove(JsonNode? parent, object key)
    {
        switch (parent)
        {
            case JsonObject owner when key is string name && owner.ContainsKey(name):
                owner.Remove(name);
                return true;
            case JsonArray list when key is int index && index < list.Count:
                list.RemoveAt(index);
                return true;
            default:
                return false;
        }
    }

    // The steps from the root to the node: property names and item indexes.
    private static List<object> PathOf(JsonNode node)
    {
        var steps = new List<object>();
        for (var at = node; at.Parent is { } parent; at = parent)
            steps.Insert(0, parent is JsonArray list ? list.IndexOf(at) : at.GetPropertyName());
        return steps;
    }

    private static JsonNode? At(JsonNode root, List<object> path)
    {
        JsonNode? found = root;
        foreach (var step in path)
            found = step is int index ? found is JsonArray list && index < list.Count ? list[index] : null : found is JsonObject owner ? owner[(string)step] : null;
        return found;
    }

    private static string Here([CallerFilePath] string here = "") => Path.GetDirectoryName(here)!;

    [GeneratedRegex(@"[/\\](?<class>M\d{5})[/\\]\w+\.cs\((?<line>\d+),\d+\): (?:error|warning) (?<code>[A-Z]+\d+): (?<message>[^\[\r\n]+)")]
    private static partial Regex BuildError();

    [GeneratedRegex(@"\b(fuzz|page|a)-[\w-]+|\b[A-Za-z]+-\d+-\w+\b")]
    private static partial Regex Ids();

    // A number, but not an error's code (CS0103, IDE0005).
    [GeneratedRegex(@"(?<![A-Z\d])\d+")]
    private static partial Regex Numbers();

    [GeneratedRegex("'[^']*'")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"^<(\w+)>")]
    private static partial Regex AngleName();
}
