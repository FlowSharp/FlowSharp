using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using FlowSharp.Application.Abstractions;
using FlowSharp.Domain.Queue;
using FlowSharp.Infrastructure.Data;

namespace FlowSharp.Infrastructure.Queue;

public abstract class EfWorkflowQueue(ApplicationDbContext dbContext) : IWorkflowQueue
{
    protected ApplicationDbContext DbContext { get; } = dbContext;

    protected virtual IsolationLevel DequeueIsolationLevel => IsolationLevel.ReadCommitted;

    public async Task<WorkflowJob> EnqueueAsync(Guid workflowId, JsonDocument payload, CancellationToken cancellationToken = default)
    {
        var job = new WorkflowJob
        {
            WorkflowId = workflowId,
            Payload = payload
        };

        DbContext.WorkflowJobs.Add(job);
        await DbContext.SaveChangesAsync(cancellationToken);

        return job;
    }

    public async Task<bool> TryEnqueueOnceAsync(Guid workflowId, JsonDocument payload, string dedupeKey, CancellationToken cancellationToken = default)
    {
        // Hizli yol: anahtar zaten varsa hic eklemeye calisma (yaygin durum).
        if (await DbContext.WorkflowJobs.AnyAsync(job => job.DedupeKey == dedupeKey, cancellationToken))
        {
            return false;
        }

        var job = new WorkflowJob
        {
            WorkflowId = workflowId,
            Payload = payload,
            DedupeKey = dedupeKey
        };

        DbContext.WorkflowJobs.Add(job);
        try
        {
            await DbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Yukaridaki kontrol ile insert arasinda baska bir ornek ayni anahtarla ekledi
            // (unique index ihlali). Cift tetikleme onlendi; eklenmemis say.
            DbContext.Entry(job).State = EntityState.Detached;
            return false;
        }
    }

    public virtual async Task<WorkflowJob?> DequeueAsync(string workerId, TimeSpan lockDuration, CancellationToken cancellationToken = default)
    {
        await using var transaction = await DbContext.Database.BeginTransactionAsync(DequeueIsolationLevel, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        // Aday isler: (1) bekleyen ve vakti gelmis isler, (2) "Processing" gorunup kilidi suresi
        // dolmus isler (worker tamamlamadan sonlanmis = terk edilmis). Ikincisi olmadan, cokmus
        // bir worker'in isi sonsuza dek "Processing" kalir ve asla yeniden denenmezdi.
        var job = await DbContext.WorkflowJobs
            .Where(job => (job.Status == WorkflowJobStatus.Pending && job.AvailableAt <= now)
                       || (job.Status == WorkflowJobStatus.Processing && job.LockedUntil != null && job.LockedUntil < now))
            .OrderBy(job => job.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        return await ClaimAsync(job, workerId, lockDuration, now, transaction, cancellationToken);
    }

    /// <summary>
    /// Secilen isi bu worker adina kilitler. Terk edilmis (yeniden alinan) ve deneme hakki
    /// tukenmis isi calistirmak yerine olu mektuba (DeadLetter) tasir; boylece worker'i tekrar
    /// tekrar cokerten "zehirli" bir is sonsuza dek geri alinmaz.
    /// </summary>
    protected async Task<WorkflowJob?> ClaimAsync(
        WorkflowJob job, string workerId, TimeSpan lockDuration, DateTimeOffset now,
        IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        var reclaimed = job.Status == WorkflowJobStatus.Processing;
        if (reclaimed && job.AttemptCount >= job.MaxAttempts)
        {
            job.Status = WorkflowJobStatus.DeadLetter;
            job.LockedBy = null;
            job.LockedUntil = null;
            job.LastError ??= "Worker isi tamamlamadan sonlandi; azami deneme sayisi asildi.";
            job.UpdatedAt = now;

            await DbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        job.Status = WorkflowJobStatus.Processing;
        job.LockedBy = workerId;
        job.LockedUntil = now.Add(lockDuration);
        job.AttemptCount++;
        job.UpdatedAt = now;

        await DbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return job;
    }

    public async Task CompleteAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await DbContext.WorkflowJobs.FindAsync([jobId], cancellationToken);
        if (job is null)
        {
            return;
        }

        job.Status = WorkflowJobStatus.Completed;
        job.LockedBy = null;
        job.LockedUntil = null;
        job.UpdatedAt = DateTimeOffset.UtcNow;

        await DbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(Guid jobId, string error, CancellationToken cancellationToken = default)
    {
        var job = await DbContext.WorkflowJobs.FindAsync([jobId], cancellationToken);
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

        await DbContext.SaveChangesAsync(cancellationToken);
    }
}
