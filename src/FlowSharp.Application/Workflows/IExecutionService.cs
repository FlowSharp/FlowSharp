using FlowSharp.Application.Security;
using FlowSharp.Domain.Workflows;

namespace FlowSharp.Application.Workflows;

/// <summary>
/// Workflow calisma (execution) kayitlarinin sahiplik kapsamli okunmasi. Sahiplik izolasyonu
/// burada zorlanir: aktor yalniz kendi workflow'larina ait calismalari gorur (Admin: tumu).
/// </summary>
public interface IExecutionService
{
    /// <summary>Aktorun gorebilecegi son calismalari yeniden eskiye dogru listeler.</summary>
    Task<IReadOnlyList<WorkflowExecution>> ListRecentAsync(
        ActorScope actor, int take = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Belirli bir workflow'a ait tek calismayi getirir. <paramref name="workflowId"/> ile kisitlandigindan
    /// baska workflow'un cikti gecmisine erisim engellenir; bulunamazsa <c>null</c>.
    /// </summary>
    Task<WorkflowExecution?> GetForWorkflowAsync(
        Guid executionId, Guid workflowId, CancellationToken cancellationToken = default);
}
