using System.Text.Json;
using System.Text.Json.Nodes;

namespace FlowSharp.Application.Nodes;

/// <summary>
/// Node'lar arasinda akan tekil veri ogesi. <c>INodeExecutionData</c>
/// karsiligidir: her oge bir <see cref="Json"/> govdesi (ve ileride binary veri)
/// tasir. Node'lar bir item listesi alir ve bir item listesi dondurur.
/// </summary>
public sealed class NodeItem
{
    public NodeItem(JsonObject? json = null)
    {
        Json = json ?? new JsonObject();
    }

    /// <summary>Ogenin JSON govdesi. Degistirilebilir (mutable) olarak tutulur.</summary>
    public JsonObject Json { get; }

    public static NodeItem Empty() => new();

    public static NodeItem From(JsonObject json) => new(json);

    /// <summary>Herhangi bir JSON degerini tek item'a sarar (obje degilse "value" alanina koyar).</summary>
    public static NodeItem FromValue(JsonNode? value)
    {
        if (value is JsonObject obj)
        {
            return new NodeItem(obj);
        }

        return new NodeItem(new JsonObject { ["value"] = value?.DeepClone() });
    }

    /// <summary>Bir JSON dokumanini item listesine cevirir: dizi ise her eleman bir item, obje ise tek item.</summary>
    public static IReadOnlyList<NodeItem> FromDocument(JsonElement element)
    {
        var node = JsonNode.Parse(element.GetRawText());

        return node switch
        {
            JsonArray array => array.Select(child => FromValue(child)).ToList(),
            JsonObject obj => [new NodeItem(obj)],
            null => [Empty()],
            _ => [FromValue(node)]
        };
    }

    public NodeItem Clone() => new((JsonObject)Json.DeepClone());
}
