using System.Text.Json;
using Cronos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FlowSharp.Application.Abstractions;
using FlowSharp.Infrastructure.Data;

namespace FlowSharp.Infrastructure.Triggers;

/// <summary>
/// Aktif workflow'lardaki schedule.trigger node'larini cron ifadelerine gore izleyip
/// vakti gelenleri kuyruga ekler. Tek worker icin uygundur; coklu worker'da ayni isin
/// birden fazla tetiklenmemesi icin ileride dagitik kilit eklenmelidir.
/// </summary>
public sealed class SchedulerService(
    IServiceScopeFactory scopeFactory,
    ILogger<SchedulerService> logger) : BackgroundService
{
    private readonly Dictionary<string, DateTime> nextRuns = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Scheduler dongusunde hata.");
            }

            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<IWorkflowQueue>();

        var workflows = await dbContext.Workflows
            .AsNoTracking()
            .Where(workflow => workflow.IsActive)
            .Select(workflow => new { workflow.Id, workflow.Definition })
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var workflow in workflows)
        {
            foreach (var (nodeName, cron) in ExtractSchedules(workflow.Definition.RootElement))
            {
                var key = $"{workflow.Id}:{nodeName}";
                seen.Add(key);

                if (!CronExpression.TryParse(cron, out var expression))
                {
                    continue;
                }

                if (!nextRuns.TryGetValue(key, out var next))
                {
                    // Ilk gorulduginde bir sonraki calisma zamani hesaplanir (gecmis tetikleme yapilmaz).
                    nextRuns[key] = expression.GetNextOccurrence(now) ?? DateTime.MaxValue;
                    continue;
                }

                if (now >= next)
                {
                    var payload = JsonDocument.Parse($$"""{"source":"trigger","node":"{{nodeName}}","firedAt":"{{now:O}}"}""");
                    await queue.EnqueueAsync(workflow.Id, payload, cancellationToken);
                    logger.LogInformation("Schedule tetiklendi: workflow {WorkflowId}, node {Node}.", workflow.Id, nodeName);
                    nextRuns[key] = expression.GetNextOccurrence(now) ?? DateTime.MaxValue;
                }
            }
        }

        // Artik var olmayan zamanlamalari temizle.
        foreach (var stale in nextRuns.Keys.Where(key => !seen.Contains(key)).ToList())
        {
            nextRuns.Remove(stale);
        }
    }

    private static IEnumerable<(string NodeName, string Cron)> ExtractSchedules(JsonElement definition)
    {
        if (!definition.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var node in nodes.EnumerateArray())
        {
            if (!node.TryGetProperty("type", out var typeEl))
            {
                continue;
            }

            // Cron tasiyan tetikleyiciler: schedule.trigger -> "cron", email.imap.trigger -> "pollCron".
            var type = typeEl.GetString();
            string cronField;
            string defaultName;
            if (string.Equals(type, "schedule.trigger", StringComparison.OrdinalIgnoreCase))
            {
                cronField = "cron";
                defaultName = "Schedule";
            }
            else if (string.Equals(type, "email.imap.trigger", StringComparison.OrdinalIgnoreCase))
            {
                cronField = "pollCron";
                defaultName = "Email Trigger";
            }
            else
            {
                continue;
            }

            var name = node.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? defaultName : defaultName;
            if (node.TryGetProperty("parameters", out var paramEl) &&
                paramEl.ValueKind == JsonValueKind.Object &&
                paramEl.TryGetProperty(cronField, out var cronEl) &&
                cronEl.ValueKind == JsonValueKind.String &&
                cronEl.GetString() is { Length: > 0 } cron)
            {
                yield return (name, cron);
            }
        }
    }
}
