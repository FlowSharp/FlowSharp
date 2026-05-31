using FlowSharp.Domain.Nodes;

namespace FlowSharp.Application.Nodes;

/// <summary>Tum calistirilabilir node'lar icin opsiyonel taban sinif.</summary>
public abstract class NodeType : INodeType
{
    public abstract NodeDefinition Definition { get; }

    public abstract Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context);
}

/// <summary>
/// En yaygin durum: her giris item'ini tek tek isleyip bir cikis item'i ureten node'lar.
/// Sadece <see cref="ProcessItemAsync"/> yazilir; bos giriste tek calisma yapilir.
/// </summary>
public abstract class PerItemNodeType : NodeType
{
    public override async Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var items = context.Items.Count > 0 ? context.Items : [NodeItem.Empty()];
        var output = new List<NodeItem>(items.Count);

        for (var index = 0; index < items.Count; index++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var result = await ProcessItemAsync(context, items[index], index);
            if (result is not null)
            {
                output.Add(result);
            }
        }

        return NodeExecutionResult.Single(output);
    }

    /// <summary>Tek bir item'i isler. null donerse oge ciktidan dusurulur (filtreleme).</summary>
    protected abstract Task<NodeItem?> ProcessItemAsync(INodeExecutionContext context, NodeItem item, int index);
}
