using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Generation;
using QaaS.Playwright.Recorder.Login;

namespace QaaS.Playwright.Tests.Recorder.Fuzz;

/// <summary>
/// The generator's foundations, each on its own: the reader, the one way a recording reaches the generator, makes any
/// recording whole and keeps every secret out of its JSON; the emitter writes any text as C#, a doc comment, a pattern
/// and YAML that each say it exactly; names and variables are never null, nor declared twice.
/// </summary>
[TestFixture]
public partial class RecordingReaderTests
{
    private static readonly HashSet<string> Kinds =
        ["click", "dblclick", "fill", "check", "uncheck", "choose", "press", "upload", "drag", "navigate", "newTab", "expect"];

    private string _folder = null!;

    [SetUp]
    public void CreateFolder() => _folder = Directory.CreateTempSubdirectory("qaas-reader-").FullName;

    [TearDown]
    public void DeleteFolder() => Directory.Delete(_folder, recursive: true);

    [Test]
    public void TheReader_MakesAnyRecordingWhole_OnceForAll_AndNoSecretIsInItsJson()
    {
        var random = new Random(1104);
        var seeds = GeneratorFuzz.Seeds().ToList();
        var problems = new List<string>();
        for (var number = 0; number < 400; number++)
        {
            var mutant = GeneratorFuzz.Mutate(seeds[number % seeds.Count], number, random);
            Recording recording;
            try
            {
                recording = Recording.FromJson(mutant.Json);
            }
            catch (JsonException)
            {
                continue;
            }

            var read = RecordingReader.Read(recording);
            var json = read.Recording.ToJson();
            foreach (var problem in Whole(read))
                problems.Add($"{mutant.ClassName} of {mutant.Seed}: {problem}");
            // Not a value an action of no secret holds as text too, as a copy of a fill into a field of no secret does.
            var open = read.Actions.Where(action => action.Secret is null).SelectMany(action => new[] { action.Value, action.Raw })
                .OfType<string>().ToHashSet();
            if (read.Actions.Select(action => action.Secret?.Reveal()).FirstOrDefault(secret => secret is { Length: >= 6 }
                    && !open.Any(text => text.Contains(secret, StringComparison.Ordinal)) && json.Contains(secret, StringComparison.Ordinal)) is { } leaked)
                problems.Add($"{mutant.ClassName} of {mutant.Seed}: the secret {leaked} is in the JSON of the recording read");
            if (RecordingReader.Read(read.Recording).Recording.ToJson() != json)
                problems.Add($"{mutant.ClassName} of {mutant.Seed}: reading it again changed it");
        }

        // An action the writer throws on is left out and said, and the rest of the flow written.
        var plan = FlowPlanner.Plan(GeneratorFuzz.Regression("SecretValues"), "Isolated");
        var broken = plan.Steps[0].Actions[0];
        broken.Locator = new CapturedCandidate { Kind = "role", Parts = [new LocatorPart { M = "no such part" }], Matches = true, Count = 1 };
        var isolated = FlowWriter.Write(plan, "Flows");
        var secret = Secret.Of("swordfish");
        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty);
            Assert.That(isolated.Checks.Select(check => check.Message), Has.One.StartsWith($"{FlowWriter.NotWritten} {broken.Source.Id}"));
            Assert.That(isolated.ActionLines.Keys, Does.Not.Contain(broken.Source.Id).And.Contains(plan.Steps[^1].Actions[^1].Source.Id));
            Assert.That(secret.ToString(), Is.EqualTo(Secret.Masked), "a secret is masked wherever it is shown");
            Assert.That($"{secret}", Does.Not.Contain("swordfish"));
            Assert.Throws<InvalidOperationException>(() => JsonSerializer.Serialize(secret), "no JSON holds a secret");
            Assert.That(secret.Reveal(), Is.EqualTo("swordfish"), "only a test run's values read it");
        });
    }

    // Texts of every kind a recording holds: quotes, braces, a QaaS variable, line breaks, controls, marks, emoji.
    private static readonly string[] Texts =
    [
        "", " ", "plain", "a'b", "\"quoted\"", "back\\slash", "{0}", "{{x}}", "${HOME}", "${NAME ?? other}", "$\"x\"", "#hash",
        "key: value", "- item", "true", "123", "1.5", "null", "~", " padded ", "line\nbreak", "cr\rlf", "tab\there", "\u0001",
        "\u200Fmarked\u200E", "\u2066isolated\u2069", "zero\u200Bwidth", "*/", "</summary>", "&amp;", "emoji \U0001F600", "\u2028",
    ];

    [TestCaseSource(nameof(Texts))]
    public void AnyText_IsWrittenAsCSharpYamlADocCommentAndAPattern_ThatEachSayItExactly(string text)
    {
        var yaml = FlowYaml.Write("Flow", [new Setting("Notes", text)]);
        var path = Path.Combine(_folder, "Flow.flow.yaml");
        File.WriteAllText(path, yaml);
        var read = LoginSettings.Read(path, "Flow")["Flow:Notes"];
        var symbols = new Symbols(["page"], ["dialog"]);
        Assert.Multiple(() =>
        {
            if (text.Contains("${", StringComparison.Ordinal))
                Assert.That((read, yaml.Contains("# Notes holds ${...}", StringComparison.Ordinal)), Is.EqualTo(((string?)null, true)),
                    "a text QaaS would read as a variable stays out of the YAML, as the code's default holds it");
            else
                Assert.That(read ?? "", Is.EqualTo(text), "the YAML reads back as the text");
            Assert.That(Unescape(CSharp.Literal(text)), Is.EqualTo(text), "the C# literal holds the text");
            Assert.That(CSharp.Literal(text), Does.Not.Match(@"[\p{Cc}\u2028\u2029\u200B-\u200F\u2066-\u2069]"), "no invisible character as it is");
            Assert.That(CSharp.Doc(text), Does.Not.Match(@"[\r\n\u2028\u2029<>]"), "a doc comment stays on its line, of no tag");
            Assert.That(Regex.IsMatch(text, $"^{CSharp.Pattern(text)}$"), Is.True, "the pattern matches the text");
            Assert.That(CSharp.Identifier(text, "Fallback"), Does.Match("^[A-Za-z_][A-Za-z0-9_]*$"), "a name is always one");
            Assert.That(Text.ReplaceWords($"{text} 9 {text}9", "9", "#"), Does.StartWith($"{text} # "), "a value replaced as a word of its own");
            Assert.That((symbols.Assign("page"), symbols.Assign("page1"), symbols.Assign("page1"), symbols.New("dialog")),
                Is.EqualTo(("page", "var page1", "page1", "dialog2")), "a variable is declared once, and a reserved name never");
        });
    }

    // The C# string literal's text, as the compiler reads its escapes.
    private static string Unescape(string literal)
    {
        var text = new StringBuilder();
        for (var index = 1; index < literal.Length - 1; index++)
        {
            if (literal[index] != '\\')
            {
                text.Append(literal[index]);
                continue;
            }

            var escape = literal[++index];
            if (escape == 'u')
            {
                text.Append((char)Convert.ToInt32(literal.Substring(index + 1, 4), 16));
                index += 4;
                continue;
            }

            text.Append(escape switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => escape });
        }

        return text.ToString();
    }

    // What the generator may count on in a recording read.
    private static IEnumerable<string> Whole(ValidRecording read)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        // A tab is opened before anything is done in it, as the flow declares its page there: the capture builds a part
        // recorded after a step from its own actions, and puts a tab it opened where the time it opened falls.
        var opened = new HashSet<string>(StringComparer.Ordinal) { "page" };
        foreach (var action in read.Actions)
        {
            if (action.Kind == "newTab") opened.Add(action.Page);
            else if (!opened.Contains(action.Page) && read.Actions.Any(other => other is { Kind: "newTab" } && other.Page == action.Page)
                     && !read.Actions.TakeWhile(other => other != action).Any(other => other.Effects.Any(effect => effect is { Kind: "popup" } && effect.Page == action.Page)))
                yield return $"{action.Id}: on {action.Page} before the tab that opens it";
            foreach (var effect in action.Effects ?? []) if (effect is { Kind: "popup", Page: { } popup }) opened.Add(popup);
            if (!ids.Add(action.Id) || action.Id.Length == 0) yield return $"id '{action.Id}' empty or given twice";
            if (!Kinds.Contains(action.Kind)) yield return $"{action.Id}: kind '{action.Kind}'";
            if (!PageName().IsMatch(action.Page)) yield return $"{action.Id}: page '{action.Page}'";
            if (action.Effects is null || action.Effects.Any(effect => effect is null || effect.Kind is null))
                yield return $"{action.Id}: effects";
            else if (action.Effects.Any(effect => effect is { Kind: "urlChanged", Url: null or "" }))
                yield return $"{action.Id}: an address change of no address";
            if (action.Dialog is { Title: null }) yield return $"{action.Id}: a dialog of no title";
            if (SecretField.Holds(action) && (action.Value is not null || action.Raw is not null))
                yield return $"{action.Id}: a secret's value as text";
            foreach (var target in new[] { action.Target, action.Field, action.Option, action.To, action.Group }.OfType<CapturedTarget>())
            {
                if (target.Candidates is null || target.Candidates.Any(candidate => candidate?.Parts is null || candidate.Parts.Any(part => part?.M is null)))
                    yield return $"{action.Id}: a target's candidates";
                if (target.Row is { Key: null or "" }) yield return $"{action.Id}: a row of no key";
            }
        }
    }

    [GeneratedRegex(@"^page\d*$")]
    private static partial Regex PageName();
}
