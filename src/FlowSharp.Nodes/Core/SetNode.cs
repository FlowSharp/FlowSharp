using System.Text.Json.Nodes;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Core;

/// <summary>
/// Item alanlarini ayarlar/uzerine yazar. "fields" parametresi bir JSON objesidir;
/// degerlerinde <c>{{ $json.x }}</c> gibi ifadeler kullanilabilir.
/// </summary>
public sealed class SetNode : PerItemNodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "set.fields",
        DisplayName: "Set",
        Category: NodeCategory.Data,
        Kind: NodeKind.Transform,
        Description: "Alanlari ayarlar veya uzerine yazar.",
        Parameters:
        [
            new NodeParameterDefinition("fields", "Fields (JSON)", NodeParameterType.Json, DefaultValue: "{}",
                HelpText: "Ayarlanacak alanlar. Ornek: {\"durum\":\"aktif\",\"ad\":\"{{$json.name}}\"}"),
            new NodeParameterDefinition("keepOnlySet", "Yalniz ayarlananlari tut", NodeParameterType.Boolean, DefaultValue: "false")
        ],
        Tags: ["data", "transform"],
        Icon: "sliders",
        Color: "#0aa06e");

    protected override Task<NodeItem?> ProcessItemAsync(INodeExecutionContext context, NodeItem item, int index)
    {
        var keepOnlySet = context.GetBoolean("keepOnlySet", index);
        var target = keepOnlySet ? new JsonObject() : (JsonObject)item.Json.DeepClone();

        if (context.GetJson("fields", index) is JsonObject fields)
        {
            var resolved = context.ResolveValue(fields, index) as JsonObject ?? fields;
            foreach (var pair in resolved)
            {
                target[pair.Key] = pair.Value?.DeepClone();
            }
        }

        return Task.FromResult<NodeItem?>(NodeItem.From(target));
    }
}
