using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FlowSharp.Application.Abstractions;

namespace FlowSharp.Infrastructure.Queue;

public class QueueWorkerService(IServiceScopeFactory scopeFactory, ILogger<QueueWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

        logger.LogInformation("QueueWorkerService baslatildi. WorkerId: {WorkerId}", workerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var queue = scope.ServiceProvider.GetRequiredService<IWorkflowQueue>();
            var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();

            var job = await queue.DequeueAsync(workerId, TimeSpan.FromMinutes(5), stoppingToken);
            if (job is null)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                continue;
            }

            try
            {
                logger.LogInformation("Job alindi ve calistiriliyor. JobId: {JobId}", job.Id);
                await runner.RunAsync(job, stoppingToken);
                await queue.CompleteAsync(job.Id, stoppingToken);
                logger.LogInformation("Job basariyla tamamlandi. JobId: {JobId}", job.Id);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Workflow job {JobId} basarisiz oldu.", job.Id);
                await queue.FailAsync(job.Id, exception.Message, stoppingToken);
            }
        }
    }
}
