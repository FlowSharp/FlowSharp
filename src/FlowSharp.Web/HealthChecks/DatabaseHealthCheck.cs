using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using FlowSharp.Infrastructure.Data;

namespace FlowSharp.Web.HealthChecks;

/// <summary>
/// Readiness kontrolu: uygulamanin kritik bagimliligi olan veritabanina ulasabildigini dogrular.
/// Baglanti kurulamiyorsa servis "hazir degil" sayilir (load balancer/k8s trafik gondermez).
/// </summary>
public sealed class DatabaseHealthCheck(ApplicationDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Veritabani baglantisi saglikli.")
                : HealthCheckResult.Unhealthy("Veritabanina baglanilamadi.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Veritabani kontrolu basarisiz.", exception);
        }
    }
}
