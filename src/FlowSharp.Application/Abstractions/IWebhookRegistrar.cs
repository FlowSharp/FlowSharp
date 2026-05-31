using System.Text.Json;

namespace FlowSharp.Application.Abstractions;

/// <summary>Workflow'lardaki webhook.trigger node'larini URL yollarina baglayan kayit yoneticisi.</summary>
public interface IWebhookRegistrar
{
    /// <summary>Bir workflow kaydedildiginde webhook kayitlarini gunceller (aktif degilse temizler).</summary>
    Task SyncAsync(Guid workflowId, JsonElement definition, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>Gelen istek icin eslesen webhook kaydini bulur.</summary>
    Task<WebhookMatch?> ResolveAsync(string method, string path, CancellationToken cancellationToken = default);
}

public sealed record WebhookMatch(Guid WorkflowId, string NodeName);
