using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using QaaS.Playwright.Configuration;

namespace QaaS.Playwright.Tests.Configuration;

public sealed class SampleSettings
{
    public int Count { get; set; } = 7;
    public bool Enabled { get; set; } = true;
    public string? Text { get; set; }
    public string[]? Names { get; set; }
    public int[]? Numbers { get; set; }
    public ContactSettings? Contact { get; set; }
    public List<ContactSettings>? Contacts { get; set; }
}

public sealed class ContactSettings
{
    [Required]
    public string? Email { get; set; }

    [System.ComponentModel.DataAnnotations.Range(1, 5)]
    public int Attempts { get; set; } = 1;
}

[TestFixture]
public class StrictBinderTests
{
    private static List<string> Problems(Dictionary<string, string?> settings, params string[] siblingKeys)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return [.. StrictBinder.Bind<SampleSettings>(configuration, "", siblingKeys).Problems.Select(problem => $"{problem}")];
    }

    [Test]
    public void Bind_ValidSettingsInAnyCase_BindsThemWithoutProblems()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["count"] = "3", ["ENABLED"] = "false", ["Names:0"] = "a", ["Contact:Email"] = "a@b.c",
        }).Build();

        var (settings, problems) = StrictBinder.Bind<SampleSettings>(configuration, "");

        Assert.That(problems, Is.Empty);
        Assert.That((settings.Count, settings.Enabled, settings.Names), Is.EqualTo((3, false, new[] { "a" })));
    }

    [Test]
    public void Bind_UnknownKey_NamesTheKnownSettings() =>
        Assert.That(Problems(new() { ["Cuont"] = "3" }),
            Is.EqualTo(new[] { "Cuont: not a setting (known: Count, Enabled, Text, Names, Numbers, Contact, Contacts)" }));

    [TestCase("Count", "seven hundred", "Count: 'seven hundred' is not a whole number")]
    [TestCase("Enabled", "offf", "Enabled: 'offf' is not true or false")]
    [TestCase("Text", "${MISSING}", "Text: '${MISSING}' holds a placeholder QaaS did not resolve")]
    [TestCase("Names", "LoginFlow", "Names: expected a list, e.g. [LoginFlow]")]
    [TestCase("Contact", "alice", "Contact: expected settings, not 'alice'")]
    public void Bind_ValueQaasWouldDropOrPassThrough_IsAProblem(string key, string value, string problem) =>
        Assert.That(Problems(new() { [key] = value }), Is.EqualTo(new[] { problem }));

    [Test]
    public void Bind_SectionThatIsASingleValue_IsAProblem()
    {
        var section = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FlowConfiguration:Checkout"] = "alice" })
            .Build().GetSection("FlowConfiguration:Checkout");

        Assert.That(StrictBinder.Bind<SampleSettings>(section, section.Path).Problems.Select(problem => $"{problem}"),
            Is.EqualTo(new[] { "FlowConfiguration:Checkout: expected settings, not 'alice'" }));
    }

    [Test]
    public void Bind_SettingsWhereAListBelongs_IsAProblem() =>
        Assert.That(Problems(new() { ["Names:LoginFlow"] = "true" }),
            Is.EqualTo(new[] { "Names: expected a list, not settings" }));

    [Test]
    public void Bind_ListWhereASingleValueBelongs_IsAProblemNotACrash() =>
        Assert.That(Problems(new() { ["Text:0"] = "a", ["Text:1"] = "b" }),
            Is.EqualTo(new[] { "Text: expected a single value, not a list or settings" }));

    [Test]
    public void Bind_EmptyItemInAListOfNumbers_IsAProblem() =>
        // QaaS would drop it and shift the next items: [1, 3, 0].
        Assert.That(Problems(new() { ["Numbers:0"] = "1", ["Numbers:1"] = "", ["Numbers:2"] = "3" }),
            Is.EqualTo(new[] { "Numbers:1: '' is not a whole number" }));

    [Test]
    public void Bind_NestedSettingsAndListItems_AreValidated()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Contact:Attempts"] = "9", ["Contacts:0:Email"] = "a@b.c", ["Contacts:1:Attempts"] = "2",
        }).Build();

        var problems = StrictBinder.Bind<SampleSettings>(configuration, "FlowConfiguration:Checkout").Problems;

        Assert.That(problems.Select(problem => problem.Path), Is.EquivalentTo(new[]
        {
            "FlowConfiguration:Checkout:Contact:Email", "FlowConfiguration:Checkout:Contact:Attempts",
            "FlowConfiguration:Checkout:Contacts:1:Email",
        }));
    }

    [Test]
    public void Bind_ValueThatDoesNotConvert_IsNotAlsoReportedAsOutOfRange() =>
        Assert.That(Problems(new() { ["Contact:Email"] = "a@b.c", ["Contact:Attempts"] = "many" }),
            Is.EqualTo(new[] { "Contact:Attempts: 'many' is not a whole number" }));

    [Test]
    public void Bind_SiblingKey_IsLeftToTheCallerAndListedAsKnown()
    {
        var problems = Problems(new() { ["FlowConfiguration:Login:User"] = "alice", ["Typo"] = "1" }, "FlowConfiguration");

        Assert.That(problems, Has.Count.EqualTo(1));
        Assert.That(problems[0], Does.StartWith("Typo: not a setting").And.EndsWith("Contacts, FlowConfiguration)"));
    }
}
