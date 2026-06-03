using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FlowSharp.Application.Abstractions;
using FlowSharp.Application.Errors;

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

            // Kilit suresi, tek bir isin makul azami sure'sinden belirgin sekilde uzun olmali:
            // aksi halde hala calisan uzun bir is (orn. Wait node, ust sinir 300s = 5 dk) kilidi
            // dolup "terk edilmis" sanilarak baska bir worker tarafindan yeniden alinir (cift
            // calistirma). 15 dk, Wait ust sinirinin uzerinde guvenli bir paydir.
            var job = await queue.DequeueAsync(workerId, TimeSpan.FromMinutes(15), stoppingToken);
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
                await queue.FailAsync(job.Id, exception.ToUserMessage(), stoppingToken);
            }
        }
    }
}
