using FlowSharp.Domain.Nodes;

namespace FlowSharp.Application.Nodes;

/// <summary>
/// Calistirilabilir bir node tipi. Yeni bir node eklemek icin tek yapilmasi gereken
/// bu arayuzu implemente eden bir sinif yazmaktir: <see cref="Definition"/> ile kendini
/// tanitir (palette/kategori/parametreler), <see cref="ExecuteAsync"/> ile calisir.
/// DI bu tipleri assembly taramasiyla otomatik kesfeder ve catalog'a ekler.
/// </summary>
public interface INodeType
{
    NodeDefinition Definition { get; }

    Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context);
}
