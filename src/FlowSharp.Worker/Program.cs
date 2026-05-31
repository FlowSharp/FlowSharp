using FlowSharp.Infrastructure;
using FlowSharp.Infrastructure.Data;
using FlowSharp.Infrastructure.Queue;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/worker-.log", rollingInterval: RollingInterval.Day));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddWorkflowScheduler();
builder.Services.AddHostedService<QueueWorkerService>();

var host = builder.Build();

if (builder.Configuration.GetValue("Database:ApplyMigrationsOnStartup", false))
{
    await host.MigrateDatabaseAsync();
}

host.Run();
