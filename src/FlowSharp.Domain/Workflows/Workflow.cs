using System.Text.Json;
using FlowSharp.Domain.Common;

namespace FlowSharp.Domain.Workflows;

public sealed class Workflow : AuditableEntity
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public int Version { get; set; } = 1;

    public JsonDocument Definition { get; set; } = JsonDocument.Parse("""{"nodes":[],"connections":[]}""");

    public ICollection<WorkflowExecution> Executions { get; } = [];
}
