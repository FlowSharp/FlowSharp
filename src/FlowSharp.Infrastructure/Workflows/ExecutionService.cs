using Microsoft.EntityFrameworkCore;
using FlowSharp.Application.Security;
using FlowSharp.Application.Workflows;
using FlowSharp.Domain.Workflows;
using FlowSharp.Infrastructure.Data;

namespace FlowSharp.Infrastructure.Workflows;

/// <inheritdoc cref="IExecutionService"/>
public sealed class ExecutionService(ApplicationDbContext dbContext) : IExecutionService
{
    public async Task<IReadOnlyList<WorkflowExecution>> ListRecentAsync(
        ActorScope actor, int take = 50, CancellationToken cancellationToken = default)
    {
        var query = dbContext.WorkflowExecutions
            .Include(execution => execution.Workflow)
            .AsQueryable();

        if (!actor.IsAdmin)
        {
            // Yalniz aktorun workflow'larina ait calismalar.
            query = query.Where(execution => execution.Workflow != null && execution.Workflow.OwnerId == actor.UserId);
        }

        return await query
            .OrderByDescending(execution => execution.CreatedAt)
            .Take(take)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<WorkflowExecution?> GetForWorkflowAsync(
        Guid executionId, Guid workflowId, CancellationToken cancellationToken = default) =>
        await dbContext.WorkflowExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                execution => execution.Id == executionId && execution.WorkflowId == workflowId,
                cancellationToken);
}
