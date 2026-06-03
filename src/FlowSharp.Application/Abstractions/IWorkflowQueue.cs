using System.Text.Json;
using FlowSharp.Domain.Queue;

namespace FlowSharp.Application.Abstractions;

public interface IWorkflowQueue
{
    Task<WorkflowJob> EnqueueAsync(Guid workflowId, JsonDocument payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verilen idempotency anahtariyla isi yalniz bir kez kuyruga ekler. Anahtar zaten varsa
    /// (baska bir ornek araya girdiyse) hicbir sey eklemez ve <c>false</c> doner. Cok-ornekli
    /// dagitimlarda zamanlanmis tetikleyicilerin mukerrer is uretmesini onler.
    /// </summary>
    Task<bool> TryEnqueueOnceAsync(Guid workflowId, JsonDocument payload, string dedupeKey, CancellationToken cancellationToken = default);

    Task<WorkflowJob?> DequeueAsync(string workerId, TimeSpan lockDuration, CancellationToken cancellationToken = default);

    Task CompleteAsync(Guid jobId, CancellationToken cancellationToken = default);

    Task FailAsync(Guid jobId, string error, CancellationToken cancellationToken = default);
}
