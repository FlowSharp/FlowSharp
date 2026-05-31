using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Core.Flow;

/// <summary>
/// Akisi belirtilen sure kadar duraklatir, sonra giris item'larini oldugu gibi gecirir.
/// (Tek-process senkron bekleme; uzun beklemeler icin ileride kuyruga erteleme eklenebilir.)
/// </summary>
public sealed class WaitNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "flow.wait",
        DisplayName: "Wait",
        Category: NodeCategory.Core,
        Kind: NodeKind.Action,
        Description: "Akisi belirtilen sure kadar bekletir.",
        Parameters:
        [
            new NodeParameterDefinition("seconds", "Saniye", NodeParameterType.Number, IsRequired: true,
                DefaultValue: "5", HelpText: "Beklenecek saniye (en fazla 300).")
        ],
        Tags: ["core", "flow"],
        Icon: "clock",
        Color: "#7d7d87");

    public override async Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var seconds = Math.Clamp(context.GetNumber("seconds", defaultValue: 5), 0, 300);
        if (seconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), context.CancellationToken);
        }

        var items = context.Items.Count > 0 ? context.Items : [NodeItem.Empty()];
        return NodeExecutionResult.Single(items);
    }
}
