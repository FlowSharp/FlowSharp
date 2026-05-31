using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Core.Flow;

/// <summary>
/// Item'lari parti parti (batch) isler ("Loop Over Items" / "Split In Batches").
/// İki cikis: <c>loop</c> (her turda mevcut parti) ve <c>done</c> (tum turlar bitince toplanan sonuc).
/// "loop" cikisina baglanan dal islenip tekrar bu node'a geri baglanmalidir; motor bu bolgeyi
/// otomatik tespit edip her parti icin yeniden calistirir.
/// </summary>
public sealed class LoopOverItemsNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "flow.loopOverItems",
        DisplayName: "Loop Over Items",
        Category: NodeCategory.Core,
        Kind: NodeKind.Transform,
        Description: "Item'lari parti parti (batch) doner.",
        Parameters:
        [
            new NodeParameterDefinition("batchSize", "Parti boyutu", NodeParameterType.Number, IsRequired: true,
                DefaultValue: "1", HelpText: "Her turda islenecek item sayisi.")
        ],
        Tags: ["core", "flow"],
        Icon: "merge",
        Color: "#7d7d87",
        Outputs:
        [
            NodePort.Named("done", "Done"),
            NodePort.Named("loop", "Loop")
        ]);

    // Motor bu node'u (tipine gore) ozel surer; yine de guvenli bir varsayilan davranis verelim.
    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var items = context.Items.Count > 0 ? context.Items : [NodeItem.Empty()];
        return Task.FromResult(NodeExecutionResult.Multi([items, []]));
    }
}
