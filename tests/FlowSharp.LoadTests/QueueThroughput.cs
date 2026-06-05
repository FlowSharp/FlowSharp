using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowSharp.Application.Abstractions;
using FlowSharp.Application.Nodes;
using FlowSharp.Application.Nodes.Agents;
using FlowSharp.Application.Workflows;
using FlowSharp.Domain.Workflows;
using FlowSharp.Infrastructure.Data;
using FlowSharp.Infrastructure.Queue;
using FlowSharp.Infrastructure.Workflows;
using FlowSharp.Infrastructure.Workflows.Expressions;
using FlowSharp.Nodes;
using FlowSharp.Nodes.Core;
using FlowSharp.Nodes.Triggers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FlowSharp.LoadTests;

/// <summary>
/// Worker'in is isleme hizini olcer: N workflow calistirmasini hem seri hem de artan
/// es zamanlilikla yapar; gercek bir SQLite dosyasi kullanir (gercek yazma-kilidi davranisi).
/// "Nerede tikaniyor?" -> SQLite tek yazardir; es zamanlilik yazimi hizlandirmaz.
/// </summary>
internal static class QueueThroughput
{
    public static async Task RunAsync(int total, int[] parallelisms)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"flowsharp-load-{Guid.NewGuid():N}.db");
        var connString = $"Data Source={dbPath}";
        try
        {
            var workflowId = await SeedAsync(connString);
            var engine = BuildEngine();

            Console.WriteLine($"\n=== Kuyruk/Worker Throughput (SQLite dosyasi) ===");
            Console.WriteLine($"Toplam calistirma: {total}\n");
            Console.WriteLine($"{"Esz.",6} | {"Sure (ms)",10} | {"Akis/sn",10} | {"Ort. ms",9} | {"p95 ms",8}");
            Console.WriteLine(new string('-', 56));

            foreach (var p in parallelisms)
            {
                var (elapsedMs, latencies) = await MeasureAsync(total, p, connString, engine, workflowId);
                var sorted = latencies.OrderBy(x => x).ToArray();
                var p95 = sorted[(int)(sorted.Length * 0.95)];
                var avg = latencies.Average();
                var perSec = total / (elapsedMs / 1000.0);
                Console.WriteLine($"{p,6} | {elapsedMs,10:F0} | {perSec,10:F0} | {avg,9:F2} | {p95,8:F2}");
            }

            Console.WriteLine("\nNot: Esz. (es zamanlilik) artinca akis/sn artmiyorsa, darbogaz");
            Console.WriteLine("paylasilan kaynaktir (burada SQLite yazma kilidi).");
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* best-effort */ }
        }
    }

    private static async Task<(double ElapsedMs, List<double> Latencies)> MeasureAsync(
        int total, int parallelism, string connString, WorkflowExecutionEngine engine, Guid workflowId)
    {
        var latencies = new System.Collections.Concurrent.ConcurrentBag<double>();
        var swTotal = Stopwatch.StartNew();

        using var throttle = new SemaphoreSlim(parallelism);
        var tasks = Enumerable.Range(0, total).Select(async _ =>
        {
            await throttle.WaitAsync();
            try
            {
                var sw = Stopwatch.StartNew();
                await RunOneAsync(connString, engine, workflowId);
                sw.Stop();
                latencies.Add(sw.Elapsed.TotalMilliseconds);
            }
            finally { throttle.Release(); }
        });

        await Task.WhenAll(tasks);
        swTotal.Stop();
        return (swTotal.Elapsed.TotalMilliseconds, latencies.ToList());
    }

    // Her calistirma kendi DbContext'ini kullanir (DbContext thread-safe degildir).
    private static async Task RunOneAsync(string connString, WorkflowExecutionEngine engine, Guid workflowId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connString).Options;
        await using var db = new ApplicationDbContext(options);
        var queue = new SqliteWorkflowQueue(db);
        var runner = new WorkflowRunner(
            db, engine, new NoopEventPublisher(), queue, new NoopRunRateLimiter(),
            Options.Create(new ExecutionOptions { SaveData = "None" }),
            NullLogger<WorkflowRunner>.Instance);

        await runner.ExecuteNowAsync(workflowId, JsonDocument.Parse("""{"source":"manual"}"""));
    }

    private static async Task<Guid> SeedAsync(string connString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connString).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var def = new JsonObject
        {
            ["nodes"] = new JsonArray(
                new JsonObject { ["id"] = "t", ["type"] = "manual.trigger", ["name"] = "T", ["parameters"] = new JsonObject() },
                new JsonObject { ["id"] = "s", ["type"] = "set.fields", ["name"] = "S",
                    ["parameters"] = new JsonObject { ["fields"] = new JsonObject { ["ok"] = "1" } } }),
            ["connections"] = new JsonArray(
                new JsonObject { ["from"] = "t", ["fromPort"] = 0, ["to"] = "s", ["toPort"] = 0 })
        };

        var wf = new Workflow { Name = "Load", IsActive = true, Definition = JsonDocument.Parse(def.ToJsonString()) };
        db.Workflows.Add(wf);
        await db.SaveChangesAsync();
        return wf.Id;
    }

    private static WorkflowExecutionEngine BuildEngine()
    {
        var registry = new NodeRegistry([new ManualTriggerNode(), new SetNode()]);
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions.AddHttpClient(services, "workflow-nodes");
        return new WorkflowExecutionEngine(
            registry, new ExpressionEvaluator(), new NoopAgent(),
            Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services),
            NullLogger<WorkflowExecutionEngine>.Instance);
    }

    private sealed class NoopEventPublisher : IWorkflowEventPublisher
    {
        public Task PublishNodeCompletedAsync(Guid workflowId, Guid executionId, NodeRunData data) => Task.CompletedTask;
    }

    private sealed class NoopRunRateLimiter : IWorkflowRunRateLimiter
    {
        public Task EnsureWithinLimitAsync(string? ownerId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopAgent : IAgentExecutor
    {
        public Task<AgentResult> ExecuteAsync(AgentRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(AgentResult.Ok(NodeItem.Empty()));
    }
}
