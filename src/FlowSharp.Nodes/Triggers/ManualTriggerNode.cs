using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Triggers;

/// <summary>Workflow'u manuel baslatan tetikleyici. Gelen trigger payload'ini ciktiya verir.</summary>
public sealed class ManualTriggerNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "manual.trigger",
        DisplayName: "Manual Trigger",
        Category: NodeCategory.Trigger,
        Kind: NodeKind.Trigger,
        Description: "Workflow'u elle calistirarak baslatir.",
        Parameters: [],
        Tags: ["trigger"],
        Icon: "play",
        Color: "#7d7d87");

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var items = context.Items.Count > 0 ? context.Items : [NodeItem.Empty()];
        return Task.FromResult(NodeExecutionResult.Single(items));
    }
}
