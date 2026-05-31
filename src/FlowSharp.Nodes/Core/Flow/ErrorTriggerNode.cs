using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Core.Flow;

/// <summary>
/// Herhangi bir workflow basarisiz oldugunda tetiklenir. Trigger payload'i hata bilgisini
/// tasir: { source:"error", workflowId, executionId, message }. Hata bildirimleri (mail/slack)
/// kurmak icin idealdir.
/// </summary>
public sealed class ErrorTriggerNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "error.trigger",
        DisplayName: "Error Trigger",
        Category: NodeCategory.Trigger,
        Kind: NodeKind.Trigger,
        Description: "Bir workflow hata verince calisir.",
        Parameters: [],
        Tags: ["trigger", "flow"],
        Icon: "trash",
        Color: "#eb5757");

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var items = context.Items.Count > 0 ? context.Items : [NodeItem.Empty()];
        return Task.FromResult(NodeExecutionResult.Single(items));
    }
}
