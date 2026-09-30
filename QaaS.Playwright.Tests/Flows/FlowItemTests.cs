using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Nodes;
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

        var items = FlowItem.AllOf("Missions", [missions], NoSessions);

        Assert.That(items.Select(item => item.Index), Is.EqualTo(new[] { 0, 1 }));
        Assert.That(items.Select(item => item.Data!["name"]!.GetValue<string>()), Is.EqualTo(new[] { "Apollo", "Gemini" }));
    }

    [Test]
    public void AllOf_DataSourceNotPassedToTheProbe_SaysHowToPassIt()
    {
        var failure = Assert.Throws<InvalidOperationException>(() =>
            FlowItem.AllOf("Missions", [ListGenerator.DataSource("Other")], NoSessions));

        Assert.That(failure!.Message, Does.Contain("'Missions'").And.Contains("DataSourceNames"));
    }

    [Test]
    public void NameOf_AddsTheItemIndex() =>
        Assert.That(new FlowItem(17, null).NameOf("CreateMissionFlow"), Is.EqualTo("CreateMissionFlow[17]"));

    [Test]
    public void ToJson_JsonBytes_AreParsed() =>
        Assert.That(FlowItem.ToJson(Encoding.UTF8.GetBytes("""{"id":7}"""))!["id"]!.GetValue<int>(), Is.EqualTo(7));

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
