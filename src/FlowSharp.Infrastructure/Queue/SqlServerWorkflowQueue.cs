using System.Data;
using Microsoft.EntityFrameworkCore;
using FlowSharp.Domain.Queue;
using FlowSharp.Infrastructure.Data;

namespace FlowSharp.Infrastructure.Queue;

/// <summary>
/// SQL Server kuyrugu. Dequeue, Postgres'in <c>FOR UPDATE SKIP LOCKED</c>'ine esdeger olan
/// <c>WITH (UPDLOCK, READPAST, ROWLOCK)</c> ipuclariyla yapilir: her worker baska bir worker'in
/// kilitledigi satiri ATLAR (cekismesiz), aldigi satiri ise kilitler (cift calistirma yok).
/// Boylece coklu worker, pessimistic <c>Serializable</c> kilidinin aksine lineer olceklenir.
/// </summary>
public sealed class SqlServerWorkflowQueue(ApplicationDbContext dbContext) : EfWorkflowQueue(dbContext)
{
    public override async Task<WorkflowJob?> DequeueAsync(
        string workerId, TimeSpan lockDuration, CancellationToken cancellationToken = default)
    {
        await using var transaction = await DbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var pending = (int)WorkflowJobStatus.Pending;
        var processing = (int)WorkflowJobStatus.Processing;

        // Aday is id'si: (1) bekleyen ve vakti gelmis, veya (2) kilidi dolmus "Processing" (terk
        // edilmis) is. READPAST kilitli satirlari atlar; UPDLOCK+ROWLOCK secilen satiri bu islem
        // commit edene dek kilitler. Interpolasyonla gelen degerler parametrelenir (injection guvenli).
        var ids = await DbContext.Database
            .SqlQuery<Guid>($@"
                SELECT TOP(1) [Id] AS [Value]
                FROM [workflow_jobs] WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE ([Status] = {pending} AND [AvailableAt] <= {now})
                   OR ([Status] = {processing} AND [LockedUntil] IS NOT NULL AND [LockedUntil] < {now})
                ORDER BY [CreatedAt]")
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        // Secilen satiri ayni islem icinde takipli olarak yukle; UPDLOCK hala tutuldugundan baska
        // worker bu satiri gormez. ClaimAsync alani gunceller, SaveChanges + Commit eder.
        var jobId = ids[0];
        var job = await DbContext.WorkflowJobs.FirstAsync(item => item.Id == jobId, cancellationToken);
        return await ClaimAsync(job, workerId, lockDuration, now, transaction, cancellationToken);
    }
}
