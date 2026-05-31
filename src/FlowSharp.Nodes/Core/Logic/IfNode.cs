using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Core.Logic;

/// <summary>
/// Kosula gore item'lari iki cikisa ayirir: 0 = true, 1 = false.
/// value1 ve value2 alanlarinda expression kullanilabilir.
/// </summary>
public sealed class IfNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "if.condition",
        DisplayName: "IF",
        Category: NodeCategory.Core,
        Kind: NodeKind.Condition,
        Description: "Kosula gore akisi true/false olarak dallandirir.",
        Parameters:
        [
            new NodeParameterDefinition("value1", "Value 1", NodeParameterType.String, IsRequired: true,
                HelpText: "Ornek: {{$json.status}}"),
            new NodeParameterDefinition("operation", "Operation", NodeParameterType.Select, IsRequired: true,
                DefaultValue: "equals", Options: ConditionEvaluator.Operations),
            new NodeParameterDefinition("value2", "Value 2", NodeParameterType.String)
        ],
        Tags: ["core", "logic"],
        Icon: "git-branch",
        Color: "#408000",
        Outputs:
        [
            NodePort.Named("true", "True"),
            NodePort.Named("false", "False")
        ]);

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var trueItems = new List<NodeItem>();
        var falseItems = new List<NodeItem>();
        var items = context.Items.Count > 0 ? context.Items : [NodeItem.Empty()];

        for (var index = 0; index < items.Count; index++)
        {
            var value1 = context.GetString("value1", index);
            var operation = context.GetString("operation", index) ?? "equals";
            var value2 = context.GetString("value2", index);

            if (ConditionEvaluator.Evaluate(value1, operation, value2))
            {
                trueItems.Add(items[index]);
            }
            else
            {
                falseItems.Add(items[index]);
            }
        }

        return Task.FromResult(NodeExecutionResult.Multi([trueItems, falseItems]));
    }
}
