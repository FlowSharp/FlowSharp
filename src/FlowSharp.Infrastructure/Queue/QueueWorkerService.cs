using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FlowSharp.Application.Abstractions;
using FlowSharp.Application.Errors;

namespace FlowSharp.Infrastructure.Queue;

public class QueueWorkerService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<QueueWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Instance basina es zamanli is sayisi. Tek worker icin paralellik: ozellikle uzun suren
        // (orn. AI/LLM) isler birbirini bloklamaz. 0/negatif degerler en az 1'e cekilir.
        var concurrency = Math.Max(1, configuration.GetValue("Worker:MaxConcurrency", 5));

        logger.LogInformation("QueueWorkerService baslatildi. Es zamanli is sayisi: {Concurrency}", concurrency);

        // Her slot kendi dequeue dongusunde, ayri WorkerId ve ayri DI scope'uyla calisir; boylece
        // FOR UPDATE SKIP LOCKED (Postgres) ile her slot farkli isi alir, cift calistirma olmaz.
        var loops = new Task[concurrency];
        for (var i = 0; i < concurrency; i++)
        {
            loops[i] = RunWorkerLoopAsync(stoppingToken);
        }

        await Task.WhenAll(loops);
    }

    private async Task RunWorkerLoopAsync(CancellationToken stoppingToken)
    {
        var workerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

        try
        {
            await DequeueLoopAsync(workerId, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Kapanis sirasinda beklenen iptal; sessizce cik.
        }
    }

    private async Task DequeueLoopAsync(string workerId, CancellationToken stoppingToken)
    {
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
