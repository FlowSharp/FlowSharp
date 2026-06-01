using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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

        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        var databaseProvider = DatabaseProviders.Normalize(configuration.GetValue<string>("Database:Provider"));

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            switch (databaseProvider)
            {
                case DatabaseProviders.SqlServer:
                    options.UseSqlServer(connectionString);
                    break;
                case DatabaseProviders.Sqlite:
                    // Web ve Worker ayni dosyaya erisir; SQLITE_BUSY'de hemen hata vermek yerine
                    // bu sure boyunca retry edilsin (es zamanli erisim icin). WAL modu migrate'te acilir.
                    var sqliteConnectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString)
                    {
                        DefaultTimeout = 30,
                        Pooling = true
                    }.ConnectionString;
                    options.UseSqlite(sqliteConnectionString);
                    break;
                default:
                    options.UseNpgsql(connectionString);
                    break;
            }
        });
        services.Configure<HttpNodeNetworkOptions>(configuration.GetSection(HttpNodeNetworkOptions.SectionName));
        services.AddTransient<PrivateNetworkBlockingHandler>();
        services.AddHttpClient("workflow-nodes")
            .AddHttpMessageHandler<PrivateNetworkBlockingHandler>();

        // Kuyruk ve calistirma
        services.AddScoped<IWorkflowQueue>(sp =>
        {
            var provider = DatabaseProviders.Normalize(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Provider);
            return provider switch
            {
                DatabaseProviders.SqlServer => ActivatorUtilities.CreateInstance<SqlServerWorkflowQueue>(sp),
                DatabaseProviders.Sqlite => ActivatorUtilities.CreateInstance<SqliteWorkflowQueue>(sp),
                _ => ActivatorUtilities.CreateInstance<PostgresWorkflowQueue>(sp)
            };
        });
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
