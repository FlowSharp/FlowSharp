using System.Text.Json;
using FlowSharp.Domain.Queue;

namespace FlowSharp.Application.Abstractions;

public interface IWorkflowQueue
{
    Task<WorkflowJob> EnqueueAsync(Guid workflowId, JsonDocument payload, CancellationToken cancellationToken = default);

    Task<WorkflowJob?> DequeueAsync(string workerId, TimeSpan lockDuration, CancellationToken cancellationToken = default);

    Task CompleteAsync(Guid jobId, CancellationToken cancellationToken = default);

    Task FailAsync(Guid jobId, string error, CancellationToken cancellationToken = default);
}
