using System.IO;
using System.Net;
using System.Net.Sockets;
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
        // Veritabani hatalarini merkezi hata ceviriciye tanit (kullanici dostu mesajlar).
        FlowSharp.Application.Errors.ErrorTranslator.Default.AddRule(
            FlowSharp.Application.Errors.ErrorRule.For<DbUpdateException>(
                "Veritabani islemi basarisiz oldu. Kayit cakismasi veya baglanti sorunu olabilir.",
                FlowSharp.Application.Errors.ErrorCategory.Data));

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        var databaseProvider = DatabaseProviders.Normalize(configuration.GetValue<string>("Database:Provider"));

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            switch (databaseProvider)
            {
                case DatabaseProviders.SqlServer:
                    options.UseSqlServer(connectionString,
                        sql => sql.MigrationsAssembly("FlowSharp.Migrations.SqlServer"));
                    break;
                case DatabaseProviders.Sqlite:
                    // Web ve Worker ayni dosyaya erisir; SQLITE_BUSY'de hemen hata vermek yerine
                    // bu sure boyunca retry edilsin (es zamanli erisim icin). WAL modu migrate'te acilir.
                    var sqliteConnectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString)
                    {
                        DefaultTimeout = 30,
                        Pooling = true
                    }.ConnectionString;
                    options.UseSqlite(sqliteConnectionString,
                        sql => sql.MigrationsAssembly("FlowSharp.Migrations.Sqlite"));
                    break;
                default:
                    options.UseNpgsql(connectionString,
                        sql => sql.MigrationsAssembly("FlowSharp.Migrations.Postgres"));
                    break;
            }
        });
        services.Configure<HttpNodeNetworkOptions>(configuration.GetSection(HttpNodeNetworkOptions.SectionName));
        services.AddTransient<PrivateNetworkBlockingHandler>();
        services.AddHttpClient("workflow-nodes")
            // Erken kontrol (literal IP/sema). Asil zorlama asagidaki ConnectCallback'te.
            .AddHttpMessageHandler<PrivateNetworkBlockingHandler>()
            // SSRF zorlamasini gercek baglanti anina tasir: her hop (redirect dahil) icin
            // hedef IP dogrulanir ve dogrulanan IP'ye pinlenerek baglanilir; boylece DNS
            // rebinding (TOCTOU) ve public->private yonlendirme ile atlatma engellenir.
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var monitor = sp.GetRequiredService<IOptionsMonitor<HttpNodeNetworkOptions>>();
                return new SocketsHttpHandler
                {
                    ConnectCallback = (context, cancellationToken) =>
                        ConnectGuardedAsync(context, monitor, cancellationToken)
                };
            });

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
        // Sahip basina dakikalik calistirma limiti (admin sahipli muaf). Singleton: sayaclar
        // process omru boyunca bellekte tutulur.
        services.Configure<Application.Workflows.RateLimitOptions>(
            configuration.GetSection(Application.Workflows.RateLimitOptions.SectionName));
        services.AddSingleton<IWorkflowRunRateLimiter, WorkflowRunRateLimiter>();
        // Buyuk execution ciktilarini DB disina tasima (offload). Varsayilan: dosya sistemi.
        services.Configure<Application.Workflows.BlobStorageOptions>(
            configuration.GetSection(Application.Workflows.BlobStorageOptions.SectionName));
        services.AddSingleton<IBlobStore, Storage.FileSystemBlobStore>();
        services.AddScoped<Application.Workflows.IWorkflowService, WorkflowService>();
        services.AddScoped<Application.Workflows.IExecutionService, ExecutionService>();
        services.AddScoped<Application.Workflows.IDashboardService, DashboardService>();
        services.AddScoped<Application.Identity.IUserDirectory, Identity.UserDirectory>();
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

    /// <summary>
    /// Cikis HTTP baglantisini DNS'i bir kez cozerek, blok modunda tum cozumlenen adresleri
    /// dogrulayarak ve yalniz dogrulanan adres(ler)e baglanarak kurar. ConnectCallback her
    /// fiziksel baglanti (ve dolayisiyla her redirect hop'u) icin tetiklendiginden, public
    /// modda private/localhost hedeflere ne dogrudan ne de yonlendirme/DNS rebinding ile ulasilamaz.
    /// </summary>
    private static async ValueTask<Stream> ConnectGuardedAsync(
        SocketsHttpConnectionContext context,
        IOptionsMonitor<HttpNodeNetworkOptions> monitor,
        CancellationToken cancellationToken)
    {
        var endpoint = context.DnsEndPoint;

        IPAddress[] addresses = IPAddress.TryParse(endpoint.Host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken);

        if (addresses.Length == 0)
        {
            throw new InvalidOperationException($"'{endpoint.Host}' icin DNS kaydi bulunamadi.");
        }

        var block = monitor.CurrentValue.ShouldBlockPrivateNetworks;
        if (block)
        {
            foreach (var address in addresses)
            {
                if (PrivateNetworkGuard.IsBlocked(address))
                {
                    throw new InvalidOperationException(
                        $"Public modda private/localhost hedeflerine HTTP istegi engellendi: {endpoint.Host}");
                }
            }
        }

        // Dogrulanan adres(ler)e pinleyerek baglan: ikinci bir DNS cozumu yok (rebinding'e kapali).
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, endpoint.Port, cancellationToken);

            // Savunma derinligi: gercekten baglanilan uzak adres de izinli olmali.
            if (block && socket.RemoteEndPoint is IPEndPoint remote && PrivateNetworkGuard.IsBlocked(remote.Address))
            {
                throw new InvalidOperationException(
                    $"Public modda private/localhost hedeflerine HTTP istegi engellendi: {endpoint.Host}");
            }

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
