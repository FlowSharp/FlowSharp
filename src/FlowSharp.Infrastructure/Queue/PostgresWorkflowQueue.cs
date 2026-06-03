using System.Data;
using Microsoft.EntityFrameworkCore;
using FlowSharp.Domain.Queue;
using FlowSharp.Infrastructure.Data;

namespace FlowSharp.Infrastructure.Queue;

/// <summary>
/// PostgreSQL kuyrugu. Dequeue, satir kilidiyle (<c>FOR UPDATE SKIP LOCKED</c>) yapilir:
/// boylece birden cok worker es zamanli calistiginda her bekleyen is yalniz tek bir
/// worker tarafindan alinir (cift islenme onlenir). Bu, ReadCommitted altinda saf
/// "SELECT sonra UPDATE" deseninin yarattigi yaris kosulunu (ayni isi iki worker'in
/// almasi) ortadan kaldirir.
/// </summary>
public sealed class PostgresWorkflowQueue(ApplicationDbContext dbContext) : EfWorkflowQueue(dbContext)
{
    public override async Task<WorkflowJob?> DequeueAsync(string workerId, TimeSpan lockDuration, CancellationToken cancellationToken = default)
    {
        await using var transaction = await DbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        // Bekleyen (vakti gelmis) VEYA terk edilmis (kilidi dolmus "Processing") en eski isi,
        // satir kilidi alarak sec. SKIP LOCKED ile baska bir worker'in kilitledigi satirlar
        // beklemeden atlanir; boylece es zamanli worker'lar ayni isi alamaz.
        var candidates = await DbContext.WorkflowJobs
            .FromSql($"""
                SELECT * FROM workflow_jobs
                WHERE ("Status" = {(int)WorkflowJobStatus.Pending} AND "AvailableAt" <= {now})
                   OR ("Status" = {(int)WorkflowJobStatus.Processing} AND "LockedUntil" IS NOT NULL AND "LockedUntil" < {now})
                ORDER BY "CreatedAt"
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        var job = candidates.FirstOrDefault();
        if (job is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        return await ClaimAsync(job, workerId, lockDuration, now, transaction, cancellationToken);
    }
}
