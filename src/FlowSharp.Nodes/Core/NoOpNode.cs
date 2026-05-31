using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Core;

/// <summary>Hicbir sey yapmaz; veriyi oldugu gibi gecirir. Akis duzenlemede ara nokta olarak kullanilir.</summary>
public sealed class NoOpNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "no.op",
        DisplayName: "No Operation",
        Category: NodeCategory.Core,
        Kind: NodeKind.Action,
        Description: "Veriyi degistirmeden gecirir.",
        Parameters: [],
        Tags: ["core"],
        Icon: "circle-dashed");

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context) =>
        Task.FromResult(NodeExecutionResult.Single(context.Items));
}
