using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Generator;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Playwright.Flows;

namespace QaaS.Playwright.Tests.Flows;

[TestFixture]
public class FlowItemTests
{
    private static readonly ImmutableList<SessionData> NoSessions = ImmutableList<SessionData>.Empty;

    [Test]
    public void AllOf_ReturnsTheGeneratedItemsInOrder()
    {
        var missions = ListGenerator.DataSource("Missions", """{"name":"Apollo"}""", """{"name":"Gemini"}""");

        var items = FlowItem.AllOf("Missions", [missions], NoSessions, new ListLogger());

        Assert.That(items.Select(item => item.Index), Is.EqualTo(new[] { 0, 1 }));
        Assert.That(items.Select(item => item.Data!["name"]!.GetValue<string>()), Is.EqualTo(new[] { "Apollo", "Gemini" }));
    }

    [Test]
    public void AllOf_DataSourceNotPassedToTheProbe_SaysHowToPassIt()
    {
        var failure = Assert.Throws<InvalidOperationException>(() =>
            FlowItem.AllOf("Missions", [ListGenerator.DataSource("Other")], NoSessions, new ListLogger()));

        Assert.That(failure!.Message, Does.Contain("'Missions'").And.Contains("DataSourceNames"));
    }

    [Test]
    public void AllOf_LazyDataSource_IsWarnedAbout()
    {
        // A Lazy source generates its items anew on every read, so whatever reads it later may see other items.
        var logger = new ListLogger();
        var missions = ListGenerator.DataSource("Missions", "{}") with { Lazy = true };

        FlowItem.AllOf("Missions", [missions], NoSessions, logger);

        Assert.That(logger.Warnings, Has.One.StartsWith("ForEach Missions is Lazy"));
    }

    [Test]
    public async Task AllOf_ProbesReadingOneDataSourceAtOnce_GetTheSameItems()
    {
        var generator = new CountingGenerator();
        var missions = new DataSource { Name = "Missions", Generator = generator };

        var reads = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            Task.Run(() => FlowItem.AllOf("Missions", [missions], NoSessions, new ListLogger()))));

        Assert.That(generator.Calls, Is.EqualTo(1), "generated once, then read from QaaS's cache");
        Assert.That(reads.Select(items => items.Single().Data!.GetValue<string>()), Has.All.EqualTo("call 1"));
    }

    [Test]
    public void NameOf_AddsTheItemIndex() =>
        Assert.That(new FlowItem(17, null).NameOf("CreateMissionFlow"), Is.EqualTo("CreateMissionFlow[17]"));

    [Test]
    public void ToJson_JsonBytes_AreParsed() =>
        Assert.That(FlowItem.ToJson(Encoding.UTF8.GetBytes("""{"id":7}"""))!["id"]!.GetValue<int>(), Is.EqualTo(7));

    [Test]
    public void ToJson_JsonBytesOfAFileWithAByteOrderMark_AreParsed()
    {
        // As File.ReadAllBytes gives them for a file saved as "UTF-8 with BOM".
        byte[] bytes = [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("""{"id":7}""")];

        Assert.That(FlowItem.ToJson(bytes)!["id"]!.GetValue<int>(), Is.EqualTo(7));
    }

    [Test]
    public void ToJson_JsonText_IsParsed() =>
        Assert.That(FlowItem.ToJson("[1,2]"), Is.InstanceOf<JsonArray>());

    [Test]
    public void ToJson_PlainText_BecomesAJsonString() =>
        Assert.That(FlowItem.ToJson("Apollo 11")!.GetValue<string>(), Is.EqualTo("Apollo 11"));

    [Test]
    public void ToJson_AnObject_IsSerialized() =>
        Assert.That(FlowItem.ToJson(new { Name = "Apollo" })!["Name"]!.GetValue<string>(), Is.EqualTo("Apollo"));

    [Test]
    public void ToJson_AJsonNode_IsCopied()
    {
        var node = new JsonObject { ["name"] = "Apollo" };

        var copy = FlowItem.ToJson(node);

        Assert.That(copy, Is.Not.SameAs(node));
        Assert.That(copy!["name"]!.GetValue<string>(), Is.EqualTo("Apollo"));
    }

    [Test]
    public void ToJson_NoBody_IsNull() => Assert.That(FlowItem.ToJson(null), Is.Null);
}

/// <summary>Takes a while to generate, and says which call generated its item.</summary>
public sealed class CountingGenerator : IGenerator
{
    private int _calls;

    public int Calls => _calls;

    public Context Context { get; set; } = null!;

    public List<ValidationResult>? LoadAndValidateConfiguration(IConfiguration configuration) => [];

    public IEnumerable<Data<object>> Generate(
        IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
    {
        var call = Interlocked.Increment(ref _calls);
        Thread.Sleep(100);
        yield return new Data<object> { Body = $"call {call}" };
    }
}
