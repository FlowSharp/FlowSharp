using System.Text.Json.Nodes;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Core.Logic;

/// <summary>
/// Item'lari kurallara gore en fazla 4 cikisa yonlendirir. "rules" parametresi
/// [{"value":"A","output":0}, ...] formatinda bir JSON dizisidir; value1 ile esit
/// olan ilk kuralin output portuna gonderilir.
/// </summary>
public sealed class SwitchNode : NodeType
{
    private const int OutputCount = 4;

    public override NodeDefinition Definition { get; } = new(
        Key: "switch.condition",
        DisplayName: "Switch",
        Category: NodeCategory.Core,
        Kind: NodeKind.Condition,
        Description: "Esleyen kurala gore item'lari farkli cikislara yonlendirir.",
        Parameters:
        [
            new NodeParameterDefinition("value1", "Value", NodeParameterType.String, IsRequired: true,
                HelpText: "Ornek: {{$json.type}}"),
            new NodeParameterDefinition("rules", "Rules (JSON)", NodeParameterType.Json,
                DefaultValue: "[{\"value\":\"a\",\"output\":0},{\"value\":\"b\",\"output\":1}]")
        ],
        Tags: ["core", "logic"],
        Icon: "bezier2",
        Color: "#408000",
        Outputs:
        [
            NodePort.Named("0", "Output 0"),
            NodePort.Named("1", "Output 1"),
            NodePort.Named("2", "Output 2"),
            NodePort.Named("3", "Output 3")
        ]);

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var outputs = new List<NodeItem>[OutputCount];
        for (var i = 0; i < OutputCount; i++)
        {
            outputs[i] = [];
        }

        var items = context.Items.Count > 0 ? context.Items : [NodeItem.Empty()];

        for (var index = 0; index < items.Count; index++)
        {
            var value1 = context.GetString("value1", index) ?? string.Empty;
            var port = ResolvePort(context.GetJson("rules", index), value1);
            if (port is >= 0 and < OutputCount)
            {
                outputs[port.Value].Add(items[index]);
            }
        }

        return Task.FromResult(NodeExecutionResult.Multi(outputs));
    }

    private static int? ResolvePort(JsonNode? rules, string value1)
    {
        if (rules is not JsonArray array)
        {
            return null;
        }

        foreach (var rule in array.OfType<JsonObject>())
        {
            var ruleValue = rule.TryGetPropertyValue("value", out var v) ? v?.ToString() : null;
            if (string.Equals(ruleValue, value1, StringComparison.OrdinalIgnoreCase))
            {
                return rule.TryGetPropertyValue("output", out var o) && o is JsonValue jv && jv.TryGetValue<int>(out var port)
                    ? port
                    : 0;
            }
        }

        return null;
    }
}
