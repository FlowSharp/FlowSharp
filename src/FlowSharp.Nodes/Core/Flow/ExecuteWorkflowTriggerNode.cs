using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Core.Flow;

/// <summary>
/// Bir alt-workflow'un giris noktasi. Baska bir workflow'daki "Execute Workflow" node'u
/// tarafindan cagrildiginda, gelen veriyi (trigger payload) akisa verir.
/// </summary>
public sealed class ExecuteWorkflowTriggerNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "flow.executeWorkflowTrigger",
        DisplayName: "Execute Workflow Trigger",
        Category: NodeCategory.Trigger,
        Kind: NodeKind.Trigger,
        Description: "Baska bir workflow tarafindan cagrildiginda baslar.",
        Parameters: [],
        Tags: ["trigger", "flow"],
        Icon: "play",
        Color: "#7d7d87");

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var items = context.Items.Count > 0 ? context.Items : [NodeItem.Empty()];
        return Task.FromResult(NodeExecutionResult.Single(items));
    }
}
