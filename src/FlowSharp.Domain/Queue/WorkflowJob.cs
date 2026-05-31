using System.Text.Json;
using FlowSharp.Domain.Common;

namespace FlowSharp.Domain.Queue;

public sealed class WorkflowJob : AuditableEntity
{
    public Guid WorkflowId { get; set; }

    public Guid? ExecutionId { get; set; }

    public WorkflowJobStatus Status { get; set; } = WorkflowJobStatus.Pending;

    public int AttemptCount { get; set; }

    public int MaxAttempts { get; set; } = 3;

    public DateTimeOffset AvailableAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LockedUntil { get; set; }

    public string? LockedBy { get; set; }

    public string? LastError { get; set; }

    public JsonDocument Payload { get; set; } = JsonDocument.Parse("{}");
}
