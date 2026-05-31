using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FlowSharp.Application.Abstractions;
using FlowSharp.Application.Workflows;
using StackExchange.Redis;

namespace FlowSharp.Infrastructure.Workflows;

public sealed class InMemoryWorkflowEventService : IWorkflowEventPublisher, IWorkflowExecutionTracker
{
    public event Func<Guid, NodeRunData, Task>? OnNodeCompleted;

    public async Task PublishNodeCompletedAsync(Guid workflowId, Guid executionId, NodeRunData data)
    {
        await NotifyNodeCompletedAsync(workflowId, data);
    }

    public async Task NotifyNodeCompletedAsync(Guid workflowId, NodeRunData data)
    {
        if (OnNodeCompleted is not null)
        {
            await OnNodeCompleted.Invoke(workflowId, data);
        }
    }
}

public sealed class RedisWorkflowEventService : BackgroundService, IWorkflowEventPublisher, IWorkflowExecutionTracker
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisWorkflowEventService> _logger;
    private const string ChannelName = "workflow:node-completed";

    public event Func<Guid, NodeRunData, Task>? OnNodeCompleted;

    public RedisWorkflowEventService(IConnectionMultiplexer redis, ILogger<RedisWorkflowEventService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task PublishNodeCompletedAsync(Guid workflowId, Guid executionId, NodeRunData data)
    {
        try
        {
            var db = _redis.GetDatabase();
            var payload = new RedisMessagePayload(workflowId, executionId, data);
            var json = JsonSerializer.Serialize(payload);
            await db.PublishAsync(RedisChannel.Literal(ChannelName), json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis'e olay gonderilirken hata olustu. WorkflowId: {WorkflowId}", workflowId);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var sub = _redis.GetSubscriber();
            await sub.SubscribeAsync(RedisChannel.Literal(ChannelName), async (channel, value) =>
            {
                if (value.IsNullOrEmpty) return;

                try
                {
                    var payload = JsonSerializer.Deserialize<RedisMessagePayload>(value.ToString());
                    if (payload is not null)
                    {
                        await NotifyNodeCompletedAsync(payload.WorkflowId, payload.Data);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Redis'ten gelen olay parse edilirken hata olustu.");
                }
            });

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis olay dinleyicisi baslatilirken hata olustu.");
        }
    }

    public async Task NotifyNodeCompletedAsync(Guid workflowId, NodeRunData data)
    {
        if (OnNodeCompleted is not null)
        {
            var delegates = OnNodeCompleted.GetInvocationList();
            foreach (var del in delegates)
            {
                try
                {
                    await ((Func<Guid, NodeRunData, Task>)del).Invoke(workflowId, data);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Olay abonesi tetiklenirken hata olustu. WorkflowId: {WorkflowId}", workflowId);
                }
            }
        }
    }

    private sealed record RedisMessagePayload(Guid WorkflowId, Guid ExecutionId, NodeRunData Data);
}
