using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Core.Logic;

/// <summary>Kosulu saglamayan item'lari eler; saglayanlari gecirir.</summary>
public sealed class FilterNode : PerItemNodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "filter.items",
        DisplayName: "Filter",
        Category: NodeCategory.Data,
        Kind: NodeKind.Transform,
        Description: "Kosula uymayan item'lari cikartir.",
        Parameters:
        [
            new NodeParameterDefinition("value1", "Value 1", NodeParameterType.String, IsRequired: true,
                HelpText: "Ornek: {{$json.amount}}"),
            new NodeParameterDefinition("operation", "Operation", NodeParameterType.Select, IsRequired: true,
                DefaultValue: "isNotEmpty", Options: ConditionEvaluator.Operations),
            new NodeParameterDefinition("value2", "Value 2", NodeParameterType.String)
        ],
        Tags: ["data", "transform"],
        Icon: "filter",
        Color: "#229954");

    protected override Task<NodeItem?> ProcessItemAsync(INodeExecutionContext context, NodeItem item, int index)
    {
        var value1 = context.GetString("value1", index);
        var operation = context.GetString("operation", index) ?? "isNotEmpty";
        var value2 = context.GetString("value2", index);

        var keep = ConditionEvaluator.Evaluate(value1, operation, value2);
        return Task.FromResult<NodeItem?>(keep ? item : null);
    }
}
