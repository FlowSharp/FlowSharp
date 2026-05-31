using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Core.Flow;

/// <summary>
/// Akisi bilerek hata ile durdurur. Workflow basarisiz isaretlenir ve (varsa) Error Trigger
/// iceren workflow'lar tetiklenir.
/// </summary>
public sealed class StopAndErrorNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "flow.stopAndError",
        DisplayName: "Stop And Error",
        Category: NodeCategory.Core,
        Kind: NodeKind.Action,
        Description: "Akisi ozel bir hata mesajiyla durdurur.",
        Parameters:
        [
            new NodeParameterDefinition("message", "Hata mesaji", NodeParameterType.String, IsRequired: true,
                DefaultValue: "Akis durduruldu.", HelpText: "Ornek: {{$json.error}}")
        ],
        Tags: ["core", "flow"],
        Icon: "trash",
        Color: "#eb5757");

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var message = context.GetString("message") ?? "Akis durduruldu.";
        throw new InvalidOperationException(message);
    }
}
