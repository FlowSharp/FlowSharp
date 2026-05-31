using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FlowSharp.Application.Abstractions;
using FlowSharp.Domain.Queue;
using FlowSharp.Infrastructure.Data;

namespace FlowSharp.Infrastructure.Queue;

public sealed class PostgresWorkflowQueue(ApplicationDbContext dbContext) : IWorkflowQueue
{
    public async Task<WorkflowJob> EnqueueAsync(Guid workflowId, JsonDocument payload, CancellationToken cancellationToken = default)
    {
        var job = new WorkflowJob
        {
            WorkflowId = workflowId,
            Payload = payload
        };

        dbContext.WorkflowJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);

        return job;
    }

    public async Task<WorkflowJob?> DequeueAsync(string workerId, TimeSpan lockDuration, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        var job = await dbContext.WorkflowJobs
            .Where(job => job.Status == WorkflowJobStatus.Pending)
            .Where(job => job.AvailableAt <= now)
            .OrderBy(job => job.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            return null;
        }

        job.Status = WorkflowJobStatus.Processing;
        job.LockedBy = workerId;
        job.LockedUntil = now.Add(lockDuration);
        job.AttemptCount++;
        job.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        return job;
    }

    public async Task CompleteAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await dbContext.WorkflowJobs.FindAsync([jobId], cancellationToken);
        if (job is null)
        {
            return;
        }

        job.Status = WorkflowJobStatus.Completed;
        job.LockedBy = null;
        job.LockedUntil = null;
        job.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(Guid jobId, string error, CancellationToken cancellationToken = default)
    {
        var job = await dbContext.WorkflowJobs.FindAsync([jobId], cancellationToken);
        if (job is null)
        {
            return;
        }

        job.LastError = error;
        job.LockedBy = null;
        job.LockedUntil = null;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        job.Status = job.AttemptCount >= job.MaxAttempts ? WorkflowJobStatus.DeadLetter : WorkflowJobStatus.Pending;
        job.AvailableAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(300, Math.Pow(2, job.AttemptCount) * 5));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
