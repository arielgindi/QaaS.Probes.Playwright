using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Playwright.Flows;

/// <summary>One item of the ForEach DataSource: its position and its body as JSON.</summary>
internal sealed record FlowItem(int Index, JsonNode? Data)
{
    /// <summary>How a flow run for this item is reported, e.g. <c>CreateMissionFlow[17]</c>.</summary>
    public string NameOf(string flowName) => $"{flowName}[{Index}]";

    /// <summary>The items the DataSource named <paramref name="name"/> generates, in order.</summary>
    public static IReadOnlyList<FlowItem> AllOf(
        string name, IEnumerable<DataSource> dataSources, IImmutableList<SessionData> sessions)
    {
        var source = dataSources.FirstOrDefault(dataSource => dataSource.Name == name)
            ?? throw new InvalidOperationException(
                $"ForEach: no DataSource named '{name}' was passed to this probe. List it in the probe's " +
                "DataSourceNames, next to ProbeConfiguration.");
        return [.. source.Retrieve(sessions).Select((data, index) => new FlowItem(index, ToJson(data.Body)))];
    }

    /// <summary>JSON text or bytes are parsed, other text becomes a JSON string, and an object is serialized.</summary>
    public static JsonNode? ToJson(object? body) => body switch
    {
        null => null,
        JsonNode node => node.DeepClone(),
        byte[] bytes => Parse(Encoding.UTF8.GetString(bytes)),
        string text => Parse(text),
        _ => JsonSerializer.SerializeToNode(body),
    };

    private static JsonNode? Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return JsonValue.Create(text);
        }
    }
}
