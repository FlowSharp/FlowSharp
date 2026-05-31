using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using FlowSharp.Application.Abstractions;
using FlowSharp.Application.Nodes;
using FlowSharp.Application.Nodes.Expressions;
using FlowSharp.Application.Workflows;
using FlowSharp.Infrastructure.Credentials;
using FlowSharp.Infrastructure.Data;
using FlowSharp.Infrastructure.Queue;
using FlowSharp.Infrastructure.Security;
using FlowSharp.Infrastructure.Triggers;
using FlowSharp.Infrastructure.Workflows;
using FlowSharp.Infrastructure.Workflows.Expressions;

namespace FlowSharp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
        services.AddHttpClient("workflow-nodes");

        // Kuyruk ve calistirma
        services.AddScoped<IWorkflowQueue, PostgresWorkflowQueue>();
        services.AddScoped<IWorkflowRunner, WorkflowRunner>();
        services.Configure<Application.Workflows.ExecutionOptions>(
            configuration.GetSection(Application.Workflows.ExecutionOptions.SectionName));
        services.AddScoped<IWorkflowExecutionEngine, WorkflowExecutionEngine>();
        services.AddSingleton<IExpressionEvaluator, ExpressionEvaluator>();

        // Redis baglantisi ve Event Tracker (Coklu Proses Canli Akis)
        try
        {
            var redisConnString = configuration.GetValue<string>("Redis:ConnectionString") ?? "localhost:6379";
            var connectionMultiplexer = ConnectionMultiplexer.Connect(redisConnString);
            services.AddSingleton<IConnectionMultiplexer>(connectionMultiplexer);
            
            services.AddSingleton<RedisWorkflowEventService>();
            services.AddSingleton<IWorkflowEventPublisher>(sp => sp.GetRequiredService<RedisWorkflowEventService>());
            services.AddSingleton<IWorkflowExecutionTracker>(sp => sp.GetRequiredService<RedisWorkflowEventService>());
            services.AddHostedService(sp => sp.GetRequiredService<RedisWorkflowEventService>());
        }
        catch (Exception)
        {
            // Redis yuklu veya acik degilse InMemory fallback calisir
            services.AddSingleton<InMemoryWorkflowEventService>();
            services.AddSingleton<IWorkflowEventPublisher>(sp => sp.GetRequiredService<InMemoryWorkflowEventService>());
            services.AddSingleton<IWorkflowExecutionTracker>(sp => sp.GetRequiredService<InMemoryWorkflowEventService>());
        }

        // Credential & trigger altyapisi
        services.AddSingleton<ICredentialProtector, CredentialProtector>();
        services.AddScoped<ICredentialStore, CredentialStore>();
        services.AddScoped<IWebhookRegistrar, WebhookRegistrar>();

        // Node ceviri deposu (built-in + plugin node cevirileri).
        services.AddSingleton<Application.Localization.INodeTranslationStore, Localization.NodeTranslationStore>();

        // RAG vektor deposu (SQLite) + ayarlar.
        services.Configure<Application.Ai.RagOptions>(configuration.GetSection(Application.Ai.RagOptions.SectionName));
        services.AddSingleton<Application.Ai.IVectorStore, Ai.SqliteVectorStore>();

        // Node altyapisi: FlowSharp.Nodes kütüphanesindeki tüm node'lar kaydedilir.
        services.AddWorkflowNodes();

        // Plugin yoneticisi: plugins/ klasorundeki topluluk node'larini Roslyn ile yukler.
        services.Configure<Application.Plugins.PluginOptions>(
            configuration.GetSection(Application.Plugins.PluginOptions.SectionName));
        services.AddSingleton<Application.Plugins.IPluginManager, Plugins.PluginManager>();

        // Dinamik Worker Kaydı (Lokalde Redis/Docker gereksinimini azaltmak için)
        var runInWebProcess = configuration.GetValue<bool>("Worker:RunInWebProcess", false);
        if (runInWebProcess)
        {
            services.AddHostedService<Queue.QueueWorkerService>();
            services.AddWorkflowScheduler();
        }

        return services;
    }

    /// <summary>Zamanlanmis tetikleyici servisini ekler (genelde Worker tarafindan cagrilir).</summary>
    public static IServiceCollection AddWorkflowScheduler(this IServiceCollection services)
    {
        services.AddHostedService<SchedulerService>();
        return services;
    }
}
