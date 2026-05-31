using FlowSharp.Application.Workflows;

namespace FlowSharp.Application.Abstractions;

/// <summary>
/// Worker veya Web icinde calisan akis sirasinda olusan dugum tamamlanma olaylarini yayinlar (Redis vb. uzerinden).
/// </summary>
public interface IWorkflowEventPublisher
{
    Task PublishNodeCompletedAsync(Guid workflowId, Guid executionId, NodeRunData data);
}

/// <summary>
/// Web katmanindaki Blazor tasarimcilarinin canlı guncellemeler alabilmesi icin olaylari dinler ve tetikler.
/// </summary>
public interface IWorkflowExecutionTracker
{
    event Func<Guid, NodeRunData, Task>? OnNodeCompleted;
    Task NotifyNodeCompletedAsync(Guid workflowId, NodeRunData data);
}
