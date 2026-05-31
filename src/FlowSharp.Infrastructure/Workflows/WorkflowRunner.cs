using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FlowSharp.Application.Abstractions;
using FlowSharp.Application.Workflows;
using FlowSharp.Domain.Queue;
using FlowSharp.Domain.Workflows;
using FlowSharp.Infrastructure.Data;

namespace FlowSharp.Infrastructure.Workflows;

/// <summary>
/// Kuyruktan gelen bir isi alir, ilgili workflow'u graf motoruyla calistirir ve
/// sonucu (cikti + node calisma gunlugu) <see cref="WorkflowExecution"/> olarak kaydeder.
/// </summary>
public sealed class WorkflowRunner(
    ApplicationDbContext dbContext,
    IWorkflowExecutionEngine engine,
    IWorkflowEventPublisher eventPublisher,
    IWorkflowQueue queue,
    IOptions<ExecutionOptions> executionOptions,
    ILogger<WorkflowRunner> logger) : IWorkflowRunner
{
    private readonly ExecutionOptions executionSettings = executionOptions.Value;

    public async Task RunAsync(WorkflowJob job, CancellationToken cancellationToken = default)
    {
        var (workflow, execution) = await PrepareExecutionAsync(job.WorkflowId, job.Payload, cancellationToken);
        job.ExecutionId = execution.Id;
        await dbContext.SaveChangesAsync(cancellationToken);

        var result = await ExecuteAndSaveAsync(workflow, execution, job.Payload, cancellationToken);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Error ?? "Workflow calismasi basarisiz.");
        }
    }

    public async Task<WorkflowRunResult> ExecuteNowAsync(Guid workflowId, JsonDocument payload, CancellationToken cancellationToken = default)
    {
        var (workflow, execution) = await PrepareExecutionAsync(workflowId, payload, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ExecuteAndSaveAsync(workflow, execution, payload, cancellationToken);
    }

    private async Task<(Workflow Workflow, WorkflowExecution Execution)> PrepareExecutionAsync(
        Guid workflowId, JsonDocument payload, CancellationToken cancellationToken)
    {
        var workflow = await dbContext.Workflows.FirstOrDefaultAsync(item => item.Id == workflowId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow '{workflowId}' bulunamadi.");

        var execution = new WorkflowExecution
        {
            WorkflowId = workflow.Id,
            Status = WorkflowExecutionStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            Input = payload
        };

        dbContext.WorkflowExecutions.Add(execution);
        return (workflow, execution);
    }

    private async Task<WorkflowRunResult> ExecuteAndSaveAsync(
        Workflow workflow, WorkflowExecution execution, JsonDocument payload, CancellationToken cancellationToken)
    {
        var options = new WorkflowExecutionOptions
        {
            WorkflowId = workflow.Id,
            OnNodeCompleted = async data =>
            {
                await eventPublisher.PublishNodeCompletedAsync(workflow.Id, execution.Id, data);
            }
        };

        try
        {
            var result = await engine.ExecuteAsync(
                workflow.Definition.RootElement,
                payload.RootElement,
                options: options,
                cancellationToken);

            // Agir node ciktilari sadece config izin verirse yazilir (metadata her zaman yazilir).
            var includeData = ShouldSaveData(result.Succeeded);
            execution.Output = BuildOutputDocument(result, includeData);
            execution.Status = result.Succeeded ? WorkflowExecutionStatus.Succeeded : WorkflowExecutionStatus.Failed;
            execution.Error = result.Error;
            execution.FinishedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Workflow {WorkflowId} calismasi {Status} ({NodeCount} node).",
                workflow.Id, execution.Status, result.Nodes.Count);

            await PruneExecutionsAsync(workflow.Id, cancellationToken);

            if (!result.Succeeded)
            {
                await TriggerErrorWorkflowsAsync(workflow.Id, execution.Id, result.Error, payload, cancellationToken);
            }

            return result;
        }
        catch (Exception exception)
        {
            execution.Status = WorkflowExecutionStatus.Failed;
            execution.Error = exception.Message;
            execution.FinishedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            await TriggerErrorWorkflowsAsync(workflow.Id, execution.Id, exception.Message, payload, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Bir workflow basarisiz oldugunda, "error.trigger" iceren aktif workflow'lari kuyruga ekler.
    /// Sonsuz donguyu onlemek icin: hata workflow'unun kendisi (error kaynakli calismalar) yeniden
    /// tetiklenmez ve basarisiz olan workflow kendini tetikleyemez.
    /// </summary>
    private async Task TriggerErrorWorkflowsAsync(
        Guid failedWorkflowId, Guid executionId, string? error, JsonDocument payload, CancellationToken cancellationToken)
    {
        try
        {
            // Bu calisma zaten bir error trigger'dan geldiyse tekrar tetikleme (recursion guard).
            if (payload.RootElement.TryGetProperty("source", out var src) &&
                string.Equals(src.GetString(), "error", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var candidates = await dbContext.Workflows
                .AsNoTracking()
                .Where(w => w.IsActive && w.Id != failedWorkflowId)
                .Select(w => new { w.Id, w.Definition })
                .ToListAsync(cancellationToken);

            foreach (var candidate in candidates)
            {
                if (!ContainsErrorTrigger(candidate.Definition.RootElement))
                {
                    continue;
                }

                var errorPayload = JsonDocument.Parse(new JsonObject
                {
                    ["source"] = "error",
                    ["workflowId"] = failedWorkflowId.ToString(),
                    ["executionId"] = executionId.ToString(),
                    ["message"] = error ?? "Workflow calismasi basarisiz."
                }.ToJsonString());

                await queue.EnqueueAsync(candidate.Id, errorPayload, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error trigger workflow'lari tetiklenirken hata.");
        }
    }

    private static bool ContainsErrorTrigger(JsonElement definition)
    {
        if (!definition.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var node in nodes.EnumerateArray())
        {
            if (node.TryGetProperty("type", out var typeEl) &&
                string.Equals(typeEl.GetString(), "error.trigger", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static JsonDocument BuildOutputDocument(WorkflowRunResult result, bool includeData)
    {
        var nodes = new JsonArray();
        foreach (var node in result.Nodes)
        {
            nodes.Add(new JsonObject
            {
                ["id"] = node.NodeId,
                ["name"] = node.NodeName,
                ["type"] = node.NodeType,
                ["status"] = node.Status.ToString(),
                ["itemCount"] = node.ItemCount,
                ["error"] = node.Error,
                ["startedAt"] = node.StartedAt.ToString("O"),
                ["finishedAt"] = node.FinishedAt.ToString("O"),
                // Agir veri yalniz includeData ise; degilse metadata kalir, cikti bos.
                ["output"] = includeData ? node.Output.DeepClone() : new JsonArray()
            });
        }

        var root = new JsonObject
        {
            ["result"] = includeData ? result.Output.DeepClone() : new JsonArray(),
            ["nodes"] = nodes
        };

        return JsonDocument.Parse(root.ToJsonString());
    }

    private bool ShouldSaveData(bool succeeded) =>
        executionSettings.SaveData?.ToLowerInvariant() switch
        {
            "none" => false,
            "errorsonly" => !succeeded,
            _ => true // "all" (varsayilan)
        };

    /// <summary>Workflow basina sayi/yas limitini asan eski calisma kayitlarini siler.</summary>
    private async Task PruneExecutionsAsync(Guid workflowId, CancellationToken cancellationToken)
    {
        try
        {
            if (executionSettings.MaxAgeDays > 0)
            {
                var cutoff = DateTimeOffset.UtcNow.AddDays(-executionSettings.MaxAgeDays);
                await dbContext.WorkflowExecutions
                    .Where(e => e.WorkflowId == workflowId && e.StartedAt < cutoff)
                    .ExecuteDeleteAsync(cancellationToken);
            }

            if (executionSettings.MaxCount > 0)
            {
                var keepIds = dbContext.WorkflowExecutions
                    .Where(e => e.WorkflowId == workflowId)
                    .OrderByDescending(e => e.StartedAt)
                    .Select(e => e.Id)
                    .Take(executionSettings.MaxCount);

                await dbContext.WorkflowExecutions
                    .Where(e => e.WorkflowId == workflowId && !keepIds.Contains(e.Id))
                    .ExecuteDeleteAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Execution budama sirasinda hata (workflow {WorkflowId}).", workflowId);
        }
    }
}
