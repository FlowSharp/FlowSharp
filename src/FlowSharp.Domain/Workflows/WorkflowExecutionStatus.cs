namespace FlowSharp.Domain.Workflows;

public enum WorkflowExecutionStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Canceled = 4
}
