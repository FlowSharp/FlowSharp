using System.Text.Json;
using System.Text.Json.Nodes;
using Jint;
using FlowSharp.Application.Json;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Developer;

/// <summary>
/// Gercek JavaScript calistiran Code node'u (Jint sandbox). Script icinde:
/// <c>items</c> (giris item dizisi, her biri {json}), <c>$json</c> (ilk item) erisilebilir.
/// Script bir dizi dondurmelidir ("Run Once for All Items" modu).
/// </summary>
public sealed class CodeNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "code.javascript",
        DisplayName: "Code",
        Category: NodeCategory.Developer,
        Kind: NodeKind.Transform,
        Description: "Sandbox icinde ozel JavaScript calistirir.",
        Parameters:
        [
            new NodeParameterDefinition("script", "JavaScript", NodeParameterType.Code, IsRequired: true,
                DefaultValue: "return items;",
                HelpText: "items: giris dizisi. Ornek: return items.map(i => ({ json: { ...i.json, ekstra: 1 } }));")
        ],
        Tags: ["developer", "code"],
        Icon: "code",
        Color: "#333333");

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var script = context.GetString("script") ?? "return items;";

        var inputArray = new JsonArray();
        foreach (var item in context.Items)
        {
            inputArray.Add(new JsonObject { ["json"] = item.Json.DeepClone() });
        }

        var engine = new Engine(options => options
            .TimeoutInterval(TimeSpan.FromSeconds(5))
            .LimitMemory(16_000_000)
            .MaxStatements(100_000));

        engine.SetValue("__input", inputArray.ToJsonString(FlowJson.Relaxed));

        var wrapped = $$"""
            JSON.stringify((function() {
                const items = JSON.parse(__input);
                const $json = items.length ? items[0].json : {};
                {{script}}
            })())
            """;

        string resultJson;
        try
        {
            var value = engine.Evaluate(wrapped);
            resultJson = value.IsUndefined() ? "null" : value.AsString();
        }
        catch (Exception exception)
        {
            return Task.FromResult(NodeExecutionResult.Failure($"Code hatasi: {exception.Message}"));
        }

        var output = ConvertResult(resultJson);
        return Task.FromResult(NodeExecutionResult.Single(output));
    }

    private static List<NodeItem> ConvertResult(string resultJson)
    {
        var node = string.IsNullOrWhiteSpace(resultJson) ? null : JsonNode.Parse(resultJson);
        var items = new List<NodeItem>();

        switch (node)
        {
            case JsonArray array:
                foreach (var element in array)
                {
                    items.Add(ToItem(element));
                }
                break;
            case JsonObject obj:
                items.Add(ToItem(obj));
                break;
            case null:
                break;
            default:
                items.Add(NodeItem.From(new JsonObject { ["value"] = node.DeepClone() }));
                break;
        }

        return items;
    }

    private static NodeItem ToItem(JsonNode? element)
    {
        // {json: {...}} sarmali veya duz obje kabul edilir.
        if (element is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("json", out var inner) && inner is JsonObject innerObj)
            {
                return NodeItem.From((JsonObject)innerObj.DeepClone());
            }
            return NodeItem.From((JsonObject)obj.DeepClone());
        }

        return NodeItem.From(new JsonObject { ["value"] = element?.DeepClone() });
    }
}
