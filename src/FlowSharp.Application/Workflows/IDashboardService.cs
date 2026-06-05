using System.Collections.Generic;
using FlowSharp.Application.Security;
using FlowSharp.Domain.Workflows;

namespace FlowSharp.Application.Workflows;

/// <summary>
/// Ana sayfa (dashboard) ozet metrikleri ve son calismalar. Sahiplik kapsami burada zorlanir:
/// aktor yalniz kendi workflow'larina ait sayim/calismalari gorur (Admin: tumu).
/// </summary>
public interface IDashboardService
{
    Task<DashboardSummary> GetSummaryAsync(ActorScope actor, CancellationToken cancellationToken = default);
}

/// <summary>Dashboard ozeti: workflow/aktif/calisma/hata sayilari ve son calismalar.</summary>
public sealed record DashboardSummary(
    int WorkflowCount,
    int ActiveCount,
    int ExecutionCount,
    int FailedCount,
    IReadOnlyList<WorkflowExecution> RecentExecutions);
