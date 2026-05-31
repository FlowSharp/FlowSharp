using System.Text.Json;
using FlowSharp.Application.Workflows;
using FlowSharp.Domain.Queue;

namespace FlowSharp.Application.Abstractions;

public interface IWorkflowRunner
{
    Task RunAsync(WorkflowJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Workflow'u kuyruga atmadan senkron calistirir, calisma kaydini olusturur ve sonucu doner.
    /// Webhook gibi cagirana aninda yanit dondurmesi gereken senaryolar icindir.
    /// </summary>
    Task<WorkflowRunResult> ExecuteNowAsync(Guid workflowId, JsonDocument payload, CancellationToken cancellationToken = default);
}
