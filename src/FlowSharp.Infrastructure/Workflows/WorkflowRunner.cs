using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FlowSharp.Application.Abstractions;
using FlowSharp.Application.Diagnostics;
using FlowSharp.Application.Errors;
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
    IWorkflowRunRateLimiter rateLimiter,
    IBlobStore blobStore,
    IOptions<ExecutionOptions> executionOptions,
    IOptions<BlobStorageOptions> blobOptions,
    ILogger<WorkflowRunner> logger) : IWorkflowRunner
{
    private readonly ExecutionOptions executionSettings = executionOptions.Value;
    private readonly BlobStorageOptions blobSettings = blobOptions.Value;

    public async Task RunAsync(WorkflowJob job, CancellationToken cancellationToken = default)
    {
        var (workflow, execution) = await PrepareExecutionAsync(job.WorkflowId, job.Payload, cancellationToken);
        job.ExecutionId = execution.Id;
        await dbContext.SaveChangesAsync(cancellationToken);

        // Kuyruk-worker yolu: arka plan, cagirana yanit dondurmez. SaveData=None ise agir veriyi
        // hic yakalamayiz (motor DeepClone'lari atlar -> yuksek throughput, dusuk bellek).
        var captureData = !string.Equals(executionSettings.SaveData, "none", StringComparison.OrdinalIgnoreCase);
        var result = await ExecuteAndSaveAsync(workflow, execution, job.Payload, captureData, cancellationToken);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Error ?? "Workflow calismasi basarisiz.");
        }
    }

    public async Task<WorkflowRunResult> ExecuteNowAsync(Guid workflowId, JsonDocument payload, CancellationToken cancellationToken = default)
    {
        var (workflow, execution) = await PrepareExecutionAsync(workflowId, payload, cancellationToken);
        // Senkron (manuel/webhook) yol kullanici/harici tetiklemedir: sahip basina dakikalik kotaya
        // tabidir (admin sahipli muaf). Limit asilmissa is olusturulmadan iptal edilir.
        await rateLimiter.EnsureWithinLimitAsync(workflow.OwnerId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        // Senkron (webhook) yol: cagirana cikti/Respond node yaniti dondurulur; veri her zaman yakalanir.
        return await ExecuteAndSaveAsync(workflow, execution, payload, captureData: true, cancellationToken);
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
        Workflow workflow, WorkflowExecution execution, JsonDocument payload, bool captureData, CancellationToken cancellationToken)
    {
        var options = new WorkflowExecutionOptions
        {
            WorkflowId = workflow.Id,
            // Calisma workflow sahibinin yetkisiyle yurur: yalniz onun credential'lari cozulebilir.
            ActorOwnerId = workflow.OwnerId,
            CaptureData = captureData,
            OnNodeCompleted = async data =>
            {
                await eventPublisher.PublishNodeCompletedAsync(workflow.Id, execution.Id, data);
            }
        };

        using var activity = FlowSharpTelemetry.ActivitySource.StartActivity("workflow.execute");
        activity?.SetTag("workflow.id", workflow.Id);
        var stopwatch = Stopwatch.StartNew();
        var statusTag = "failed";
        try
        {
            var result = await engine.ExecuteAsync(
                workflow.Definition.RootElement,
                payload.RootElement,
                options: options,
                cancellationToken);
            statusTag = result.Succeeded ? "succeeded" : "failed";

            // Agir node ciktilari sadece config izin verirse yazilir (metadata her zaman yazilir).
            var includeData = ShouldSaveData(result.Succeeded);
            execution.Output = await OffloadIfLargeAsync(BuildOutputDocument(result, includeData), cancellationToken);
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
            var message = exception.ToUserMessage();
            execution.Status = WorkflowExecutionStatus.Failed;
            execution.Error = message;
            execution.FinishedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            await TriggerErrorWorkflowsAsync(workflow.Id, execution.Id, message, payload, cancellationToken);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            var statusKvp = new KeyValuePair<string, object?>("status", statusTag);
            FlowSharpTelemetry.WorkflowRuns.Add(1, statusKvp);
            FlowSharpTelemetry.WorkflowDuration.Record(stopwatch.Elapsed.TotalMilliseconds, statusKvp);
            activity?.SetTag("workflow.status", statusTag);
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

    /// <summary>
    /// Cikti DB'ye yazilmadan once, offload etkin ve icerik esigi asiyorsa blob deposuna tasinir;
    /// DB'de yalniz kucuk bir referans isaretcisi kalir. Aksi halde belge oldugu gibi doner.
    /// </summary>
    private async Task<JsonDocument> OffloadIfLargeAsync(JsonDocument output, CancellationToken cancellationToken)
    {
        if (!blobSettings.Enabled)
        {
            return output;
        }

        var json = output.RootElement.GetRawText();
        if (Encoding.UTF8.GetByteCount(json) <= blobSettings.ThresholdBytes)
        {
            return output;
        }

        var reference = await blobStore.SaveAsync(json, cancellationToken);
        logger.LogInformation("Execution ciktisi blob deposuna tasindi ({Bytes} byte, ref {Reference}).",
            json.Length, reference);
        return ExecutionOutputBlob.CreateMarker(reference);
    }

    /// <summary>Workflow basina sayi/yas limitini asan eski calisma kayitlarini (ve offload blob'larini) siler.</summary>
    private async Task PruneExecutionsAsync(Guid workflowId, CancellationToken cancellationToken)
    {
        try
        {
            if (executionSettings.MaxAgeDays > 0)
            {
                var cutoff = DateTimeOffset.UtcNow.AddDays(-executionSettings.MaxAgeDays);
                var aged = dbContext.WorkflowExecutions
                    .Where(e => e.WorkflowId == workflowId && e.StartedAt < cutoff);

                await DeleteOffloadedBlobsAsync(aged, cancellationToken);
                await aged.ExecuteDeleteAsync(cancellationToken);
            }

            if (executionSettings.MaxCount > 0)
            {
                var keepIds = dbContext.WorkflowExecutions
                    .Where(e => e.WorkflowId == workflowId)
                    .OrderByDescending(e => e.StartedAt)
                    .Select(e => e.Id)
                    .Take(executionSettings.MaxCount);

                var excess = dbContext.WorkflowExecutions
                    .Where(e => e.WorkflowId == workflowId && !keepIds.Contains(e.Id));

                await DeleteOffloadedBlobsAsync(excess, cancellationToken);
                await excess.ExecuteDeleteAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Execution budama sirasinda hata (workflow {WorkflowId}).", workflowId);
        }
    }

    /// <summary>
    /// Silinecek calismalardan offload edilmis olanlarin blob'larini siler (yetim blob birakmamak icin).
    /// Offload kapaliyken hicbir ek sorgu yapilmaz; varsayilan budama yolu degismez.
    /// </summary>
    private async Task DeleteOffloadedBlobsAsync(IQueryable<WorkflowExecution> toDelete, CancellationToken cancellationToken)
    {
        if (!blobSettings.Enabled)
        {
            return;
        }

        var outputs = await toDelete.AsNoTracking().Select(e => e.Output).ToListAsync(cancellationToken);
        foreach (var output in outputs)
        {
            if (ExecutionOutputBlob.TryGetReference(output, out var reference))
            {
                await blobStore.DeleteAsync(reference, cancellationToken);
            }
        }
    }
}
