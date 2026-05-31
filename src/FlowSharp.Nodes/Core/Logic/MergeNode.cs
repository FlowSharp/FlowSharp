using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Core.Logic;

/// <summary>
/// Birden cok daldan gelen item'lari tek cikista birlestirir. Motor, gelen tum
/// baglantilarin item'larini zaten bu node'un girisinde topladigi icin burada
/// birlesik liste oldugu gibi gecirilir.
/// </summary>
public sealed class MergeNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "merge.items",
        DisplayName: "Merge",
        Category: NodeCategory.Core,
        Kind: NodeKind.Transform,
        Description: "Iki veya daha fazla dali tek akista birlestirir.",
        Parameters: [],
        Tags: ["core", "logic"],
        Icon: "git-merge",
        Color: "#9b59b6");

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context) =>
        Task.FromResult(NodeExecutionResult.Single(context.Items));
}
