using System.Text.Json;
using FluentAssertions;
using FlowSharp.Domain.Queue;
using FlowSharp.Infrastructure.Queue;
using FlowSharp.Tests.Fixtures;
using Xunit;

namespace FlowSharp.Tests.Infrastructure;

public class WorkflowQueueTests : IDisposable
{
    private readonly SqliteDbFixture db = new();

    public void Dispose() => db.Dispose();

    private SqliteWorkflowQueue NewQueue() => new(db.NewContext());

    private static JsonDocument Payload() => JsonDocument.Parse("""{"source":"manual"}""");

    [Fact]
    public async Task Enqueue_then_dequeue_returns_pending_job_and_locks_it()
    {
        var workflowId = Guid.NewGuid();
        var enqueued = await NewQueue().EnqueueAsync(workflowId, Payload());

        var dequeued = await NewQueue().DequeueAsync("worker-1", TimeSpan.FromMinutes(1));

        dequeued.Should().NotBeNull();
        dequeued!.Id.Should().Be(enqueued.Id);
        dequeued.Status.Should().Be(WorkflowJobStatus.Processing);
        dequeued.LockedBy.Should().Be("worker-1");
        dequeued.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task Dequeue_returns_null_when_no_pending_jobs()
    {
        var result = await NewQueue().DequeueAsync("worker-x", TimeSpan.FromMinutes(1));
        result.Should().BeNull();
    }

    [Fact]
    public async Task Complete_marks_job_completed()
    {
        var job = await NewQueue().EnqueueAsync(Guid.NewGuid(), Payload());
        await NewQueue().DequeueAsync("w", TimeSpan.FromMinutes(1));

        await NewQueue().CompleteAsync(job.Id);

        await using var ctx = db.NewContext();
        var reloaded = await ctx.WorkflowJobs.FindAsync(job.Id);
        reloaded!.Status.Should().Be(WorkflowJobStatus.Completed);
        reloaded.LockedBy.Should().BeNull();
    }

    [Fact]
    public async Task Fail_requeues_until_max_attempts_then_dead_letters()
    {
        var job = await NewQueue().EnqueueAsync(Guid.NewGuid(), Payload());

        for (var attempt = 0; attempt < job.MaxAttempts; attempt++)
        {
            await NewQueue().DequeueAsync("w", TimeSpan.FromMinutes(1));
            await NewQueue().FailAsync(job.Id, "patladi");

            // Backoff gecikmesini atla: isi yeniden hemen alinabilir yap.
            await using var resetCtx = db.NewContext();
            var pending = await resetCtx.WorkflowJobs.FindAsync(job.Id);
            pending!.AvailableAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await resetCtx.SaveChangesAsync();
        }

        await using var ctx = db.NewContext();
        var reloaded = await ctx.WorkflowJobs.FindAsync(job.Id);
        reloaded!.Status.Should().Be(WorkflowJobStatus.DeadLetter);
        reloaded.LastError.Should().Be("patladi");
    }

    [Fact]
    public async Task Dequeue_respects_fifo_by_created_at()
    {
        var first = await NewQueue().EnqueueAsync(Guid.NewGuid(), Payload());
        await Task.Delay(10);
        await NewQueue().EnqueueAsync(Guid.NewGuid(), Payload());

        var dequeued = await NewQueue().DequeueAsync("w", TimeSpan.FromMinutes(1));
        dequeued!.Id.Should().Be(first.Id);
    }

    [Fact]
    public async Task Dequeue_reclaims_processing_job_whose_lock_expired()
    {
        // Bir is alindi (Processing) ama worker cokup tamamlayamadi: kilit suresi gecmise cekiliyor.
        var job = await NewQueue().EnqueueAsync(Guid.NewGuid(), Payload());
        await NewQueue().DequeueAsync("crashed-worker", TimeSpan.FromMinutes(5));

        await using (var ctx = db.NewContext())
        {
            var stuck = await ctx.WorkflowJobs.FindAsync(job.Id);
            stuck!.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(-1);
            await ctx.SaveChangesAsync();
        }

        var reclaimed = await NewQueue().DequeueAsync("healthy-worker", TimeSpan.FromMinutes(5));

        reclaimed.Should().NotBeNull();
        reclaimed!.Id.Should().Be(job.Id);
        reclaimed.LockedBy.Should().Be("healthy-worker");
        reclaimed.AttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task Dequeue_does_not_reclaim_processing_job_with_active_lock()
    {
        var job = await NewQueue().EnqueueAsync(Guid.NewGuid(), Payload());
        await NewQueue().DequeueAsync("busy-worker", TimeSpan.FromMinutes(5));

        // Kilit hala gecerli (gelecekte): is hala calisiyor, geri alinmamali.
        var result = await NewQueue().DequeueAsync("other-worker", TimeSpan.FromMinutes(5));

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryEnqueueOnce_enqueues_first_call_and_skips_duplicate_key()
    {
        var workflowId = Guid.NewGuid();
        const string key = "schedule:wf:Schedule:638000000000000000";

        var first = await NewQueue().TryEnqueueOnceAsync(workflowId, Payload(), key);
        var second = await NewQueue().TryEnqueueOnceAsync(workflowId, Payload(), key);

        first.Should().BeTrue();
        second.Should().BeFalse();

        await using var ctx = db.NewContext();
        var count = ctx.WorkflowJobs.Count(job => job.DedupeKey == key);
        count.Should().Be(1);
    }

    [Fact]
    public async Task TryEnqueueOnce_allows_different_keys()
    {
        var workflowId = Guid.NewGuid();

        var a = await NewQueue().TryEnqueueOnceAsync(workflowId, Payload(), "schedule:wf:n:1");
        var b = await NewQueue().TryEnqueueOnceAsync(workflowId, Payload(), "schedule:wf:n:2");

        a.Should().BeTrue();
        b.Should().BeTrue();
    }

    [Fact]
    public async Task Plain_enqueue_leaves_dedupe_key_null_and_allows_many()
    {
        // Manuel/webhook enqueue'lar tekillestirilmez: birden cok null DedupeKey serbest olmali.
        await NewQueue().EnqueueAsync(Guid.NewGuid(), Payload());
        await NewQueue().EnqueueAsync(Guid.NewGuid(), Payload());

        await using var ctx = db.NewContext();
        var nullKeyCount = ctx.WorkflowJobs.Count(job => job.DedupeKey == null);
        nullKeyCount.Should().Be(2);
    }

    [Fact]
    public async Task Dequeue_dead_letters_reclaimed_job_when_attempts_exhausted()
    {
        var job = await NewQueue().EnqueueAsync(Guid.NewGuid(), Payload());

        // Deneme hakki tukenmis ve kilidi dolmus terk edilmis is: geri alinmak yerine olu mektuba gider.
        await using (var ctx = db.NewContext())
        {
            var stuck = await ctx.WorkflowJobs.FindAsync(job.Id);
            stuck!.Status = WorkflowJobStatus.Processing;
            stuck.AttemptCount = stuck.MaxAttempts;
            stuck.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(-1);
            await ctx.SaveChangesAsync();
        }

        var result = await NewQueue().DequeueAsync("worker", TimeSpan.FromMinutes(5));

        result.Should().BeNull();

        await using var verify = db.NewContext();
        var reloaded = await verify.WorkflowJobs.FindAsync(job.Id);
        reloaded!.Status.Should().Be(WorkflowJobStatus.DeadLetter);
    }
}
