using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlowSharp.Infrastructure.Data;

public static class DatabaseMigrationExtensions
{
    public static async Task MigrateDatabaseAsync(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (dbContext.Database.IsNpgsql())
        {
            logger.LogInformation("Applying pending EF Core migrations.");
            await dbContext.Database.MigrateAsync();
            logger.LogInformation("EF Core migrations are up to date.");
            return;
        }

        logger.LogWarning(
            "No provider-specific migrations are available for {Provider}. Ensuring database schema exists.",
            dbContext.Database.ProviderName);
        await dbContext.Database.EnsureCreatedAsync();
        logger.LogInformation("Database schema exists.");
    }
}
