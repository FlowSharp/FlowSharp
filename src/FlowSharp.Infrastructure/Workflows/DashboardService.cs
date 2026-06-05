using Microsoft.EntityFrameworkCore;
using FlowSharp.Application.Security;
using FlowSharp.Application.Workflows;
using FlowSharp.Domain.Workflows;
using FlowSharp.Infrastructure.Data;

namespace FlowSharp.Infrastructure.Workflows;

/// <inheritdoc cref="IDashboardService"/>
public sealed class DashboardService(ApplicationDbContext dbContext) : IDashboardService
{
    public async Task<DashboardSummary> GetSummaryAsync(
        ActorScope actor, CancellationToken cancellationToken = default)
    {
        // Sahiplik filtresi: Admin tum kayitlari, digerleri yalniz kendi workflow'larini/calismalarini gorur.
        var workflows = dbContext.Workflows.AsQueryable();
        var executions = dbContext.WorkflowExecutions.AsQueryable();
        if (!actor.IsAdmin)
        {
            workflows = workflows.Where(workflow => workflow.OwnerId == actor.UserId);
            executions = executions.Where(execution =>
                execution.Workflow != null && execution.Workflow.OwnerId == actor.UserId);
        }

        var workflowCount = await workflows.CountAsync(cancellationToken);
        var activeCount = await workflows.CountAsync(workflow => workflow.IsActive, cancellationToken);
        var executionCount = await executions.CountAsync(cancellationToken);
        var failedCount = await executions.CountAsync(
            execution => execution.Status == WorkflowExecutionStatus.Failed, cancellationToken);
        var recent = await executions
            .Include(execution => execution.Workflow)
            .OrderByDescending(execution => execution.CreatedAt)
            .Take(8)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return new DashboardSummary(workflowCount, activeCount, executionCount, failedCount, recent);
    }
}
