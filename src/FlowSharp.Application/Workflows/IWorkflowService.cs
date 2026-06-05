using System.Text.Json;
using FlowSharp.Application.Security;
using FlowSharp.Domain.Workflows;

namespace FlowSharp.Application.Workflows;

/// <summary>
/// Workflow yasam dongusunun (listele/getir/kaydet/sil/calistir) tek is mantigi noktasi.
/// Sahiplik (owner) izolasyonu ve webhook senkronizasyonu burada zorlanir; boylece hem
/// Blazor UI hem de ileride eklenecek API ayni guvenlik sinirini paylasir.
/// </summary>
public interface IWorkflowService
{
    /// <summary>Aktorun gorebilecegi workflow'lari yeniden eskiye dogru listeler (Admin: tumu).</summary>
    Task<IReadOnlyList<Workflow>> ListAsync(ActorScope actor, CancellationToken cancellationToken = default);

    /// <summary>Duzenleme icin workflow'u getirir. Erisim yoksa veya kayit yoksa <c>null</c> doner.</summary>
    Task<Workflow?> GetForEditAsync(Guid id, ActorScope actor, CancellationToken cancellationToken = default);

    /// <summary>Workflow aktore (ya da Admin'e) ait mi?</summary>
    Task<bool> OwnsAsync(Guid id, ActorScope actor, CancellationToken cancellationToken = default);

    /// <summary>Workflow'u senkron calistirir. Aktor sahibi (ya da Admin) degilse engellenir.</summary>
    Task<WorkflowRunResult> RunAsync(
        Guid id, JsonDocument payload, ActorScope actor, CancellationToken cancellationToken = default);

    /// <summary>Workflow'u ve webhook kayitlarini siler. Aktor sahibi (ya da Admin) degilse engellenir.</summary>
    Task DeleteAsync(Guid id, ActorScope actor, CancellationToken cancellationToken = default);

    /// <summary>Workflow'u olusturur veya gunceller, webhook kayitlarini senkronlar ve sonucu doner.</summary>
    Task<WorkflowSaveResult> SaveAsync(
        WorkflowSaveInput input, ActorScope actor, CancellationToken cancellationToken = default);

    /// <summary>Workflow'a atanmis webhook anahtarini (varsa) doner; URL uretimi icin kullanilir.</summary>
    Task<string?> GetWebhookKeyAsync(Guid workflowId, CancellationToken cancellationToken = default);
}

/// <summary>Kaydetme girdisi. <see cref="Id"/> null ise yeni kayit (aktore atanir), degilse guncelleme.</summary>
public sealed record WorkflowSaveInput(
    Guid? Id, string Name, string? Description, bool IsActive, JsonDocument Definition);

/// <summary>Kaydetme sonucu: olusan/guncellenen workflow'un kimligi ve guncel webhook anahtari.</summary>
public sealed record WorkflowSaveResult(Guid Id, string? WebhookKey);
