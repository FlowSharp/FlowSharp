using System.Text.Json;
using FlowSharp.Domain.Common;

namespace FlowSharp.Domain.Workflows;

public sealed class WorkflowExecution : AuditableEntity
{
    public Guid WorkflowId { get; set; }

    public Workflow? Workflow { get; set; }

    public WorkflowExecutionStatus Status { get; set; } = WorkflowExecutionStatus.Queued;

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public string? Error { get; set; }

    public JsonDocument Input { get; set; } = JsonDocument.Parse("{}");

    public JsonDocument Output { get; set; } = JsonDocument.Parse("{}");
}
