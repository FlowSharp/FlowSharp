namespace FlowSharp.Domain.Queue;

public enum WorkflowJobStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    DeadLetter = 4,
    Canceled = 5
}
