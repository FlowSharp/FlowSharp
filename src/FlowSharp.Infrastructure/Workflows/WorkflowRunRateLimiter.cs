using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using FlowSharp.Application.Abstractions;
using FlowSharp.Application.Workflows;
using FlowSharp.Infrastructure.Data;
using FlowSharp.Infrastructure.Identity;

namespace FlowSharp.Infrastructure.Workflows;

/// <summary>
/// Bellek ici, sahip (owner) basina kayan pencere (sliding window) limit uygulamasi. Singleton'dir:
/// sayaclar process omru boyunca tutulur. Admin sahipli workflow'lar ve sahipsiz calismalar muaftir;
/// admin kullanici kimlikleri kisa TTL ile onbellege alinir (her istekte DB sorgusu yapilmaz).
/// </summary>
public sealed class WorkflowRunRateLimiter(
    IServiceScopeFactory scopeFactory,
    IOptions<RateLimitOptions> options) : IWorkflowRunRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan AdminCacheTtl = TimeSpan.FromMinutes(1);

    private readonly RateLimitOptions settings = options.Value;
    private readonly ConcurrentDictionary<string, Queue<DateTime>> hits = new(StringComparer.Ordinal);

    private volatile HashSet<string> adminIds = new(StringComparer.Ordinal);
    private DateTime adminIdsExpiry = DateTime.MinValue;
    private readonly SemaphoreSlim adminRefreshLock = new(1, 1);

    public async Task EnsureWithinLimitAsync(string? ownerId, CancellationToken cancellationToken = default)
    {
        // Kapali, sahipsiz (sistem) veya admin sahipli -> muaf.
        if (!settings.Enabled || string.IsNullOrEmpty(ownerId))
        {
            return;
        }

        if (await IsAdminAsync(ownerId, cancellationToken))
        {
            return;
        }

        var limit = settings.RunsPerMinutePerUser;
        if (limit <= 0)
        {
            return; // 0/negatif = limitsiz.
        }

        var now = DateTime.UtcNow;
        var bucket = hits.GetOrAdd(ownerId, _ => new Queue<DateTime>());
        lock (bucket)
        {
            // Pencere disindaki eski kayitlari at.
            while (bucket.Count > 0 && now - bucket.Peek() >= Window)
            {
                bucket.Dequeue();
            }

            if (bucket.Count >= limit)
            {
                throw new WorkflowRateLimitedException(
                    $"Calistirma limiti asildi (dakikada en fazla {limit}). Lutfen biraz sonra tekrar deneyin.");
            }

            bucket.Enqueue(now);
        }
    }

    private async Task<bool> IsAdminAsync(string ownerId, CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow >= adminIdsExpiry)
        {
            await RefreshAdminIdsAsync(cancellationToken);
        }

        return adminIds.Contains(ownerId);
    }

    private async Task RefreshAdminIdsAsync(CancellationToken cancellationToken)
    {
        await adminRefreshLock.WaitAsync(cancellationToken);
        try
        {
            // Baska bir cagiran bekleme sirasinda tazelemis olabilir.
            if (DateTime.UtcNow < adminIdsExpiry)
            {
                return;
            }

            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var adminRoleId = await dbContext.Roles
                .Where(role => role.Name == IdentitySeeder.AdminRole)
                .Select(role => role.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var ids = adminRoleId is null
                ? []
                : await dbContext.UserRoles
                    .Where(userRole => userRole.RoleId == adminRoleId)
                    .Select(userRole => userRole.UserId)
                    .ToListAsync(cancellationToken);

            adminIds = new HashSet<string>(ids, StringComparer.Ordinal);
            adminIdsExpiry = DateTime.UtcNow.Add(AdminCacheTtl);
        }
        finally
        {
            adminRefreshLock.Release();
        }
    }
}
