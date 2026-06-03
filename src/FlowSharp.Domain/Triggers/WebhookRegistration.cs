using FlowSharp.Domain.Common;

namespace FlowSharp.Domain.Triggers;

/// <summary>
/// Bir workflow'daki webhook.trigger node'unu URL yoluna baglayan kayit.
/// Gelen istekler bu tabloya gore ilgili workflow'a yonlendirilir.
/// </summary>
public sealed class WebhookRegistration : AuditableEntity
{
    public Guid WorkflowId { get; set; }

    /// <summary>
    /// Workflow'a ozel, degismez webhook kilit anahtari. Gelen istekler /webhook/{WorkflowKey}/{Path}
    /// ile bu kayda eslenir; her workflow benzersiz bir anahtar aldigindan ayni Path farkli
    /// workflow'larda (ayni kullanicininkiler dahil) cakismaz.
    /// </summary>
    public string? WorkflowKey { get; set; }

    public required string NodeName { get; set; }

    public required string Method { get; set; }

    public required string Path { get; set; }

    public bool IsActive { get; set; } = true;
}
