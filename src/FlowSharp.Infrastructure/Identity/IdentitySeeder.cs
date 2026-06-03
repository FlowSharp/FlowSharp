using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using FlowSharp.Application.Abstractions;
using FlowSharp.Domain.Security;
using FlowSharp.Infrastructure.Data;

namespace FlowSharp.Infrastructure.Identity;

/// <summary>
/// Baslangic rollerini (Admin/Editor/Viewer), bunlara bagli permission claim'lerini ve
/// ilk admin kullanicisini olusturur. Rollerin permission claim'leri kullanici principal'ina
/// otomatik yansir; boylece policy'ler (RequireClaim "permission") calisir.
/// </summary>
public static class IdentitySeeder
{
    public const string AdminRole = "Admin";
    public const string EditorRole = "Editor";
    public const string ViewerRole = "Viewer";

    /// <summary>Self-registration ile kayit olan kullanicilara otomatik atanan rol.
    /// Kendi workflow'larini olusturup calistirabilir; baskasinin kayitlarini goremez (sahiplik filtresi).</summary>
    public const string MemberRole = "Member";

    private static readonly Dictionary<string, string[]> RolePermissions = new()
    {
        [AdminRole] = AppPermissions.All,
        // Editor ve Member: kendi workflow'larini ve kendi credential'larini yonetir
        // (credentials.manage non-admin'de owner-scope'tur; sayfa/store yalniz kendi kayitlarini gosterir).
        [EditorRole] = [AppPermissions.WorkflowsRead, AppPermissions.WorkflowsWrite, AppPermissions.WorkflowsExecute, AppPermissions.ExecutionsRead, AppPermissions.CredentialsManage],
        [MemberRole] = [AppPermissions.WorkflowsRead, AppPermissions.WorkflowsWrite, AppPermissions.WorkflowsExecute, AppPermissions.ExecutionsRead, AppPermissions.CredentialsManage],
        [ViewerRole] = [AppPermissions.WorkflowsRead, AppPermissions.ExecutionsRead]
    };

    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("IdentitySeeder");
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var (roleName, permissions) in RolePermissions)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                role = new IdentityRole(roleName);
                await roleManager.CreateAsync(role);
            }

            var existingClaims = (await roleManager.GetClaimsAsync(role))
                .Where(claim => claim.Type == "permission")
                .Select(claim => claim.Value)
                .ToHashSet();

            foreach (var permission in permissions.Where(permission => !existingClaims.Contains(permission)))
            {
                await roleManager.AddClaimAsync(role, new System.Security.Claims.Claim("permission", permission));
            }
        }

        // Ilk admin kullanicisi (yalniz hic kullanici yoksa).
        var adminEmail = configuration["Seed:Admin:Email"] ?? "admin@FlowSharp.local";
        var adminPassword = configuration["Seed:Admin:Password"] ?? "Admin!2345";

        if (await userManager.FindByEmailAsync(adminEmail) is null && !userManager.Users.Any())
        {
            var admin = new ApplicationUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
            var result = await userManager.CreateAsync(admin, adminPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, AdminRole);
                logger.LogInformation("Ilk admin kullanicisi olusturuldu: {Email}", adminEmail);
            }
            else
            {
                logger.LogWarning("Admin kullanicisi olusturulamadi: {Errors}", string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }

        // Aktif webhook registration'larina workflow'a ozel key atamak icin yeniden senkronla
        // (key'i olmayan/eski kayitlar boylece otomatik anahtar kazanir; yeniden kaydetme gerekmez).
        await BackfillWebhookRegistrationsAsync(sp, logger);
    }

    private static async Task BackfillWebhookRegistrationsAsync(IServiceProvider sp, ILogger logger)
    {
        var dbContext = sp.GetRequiredService<ApplicationDbContext>();
        var registrar = sp.GetRequiredService<IWebhookRegistrar>();

        var activeWebhookWorkflows = await dbContext.Workflows
            .AsNoTracking()
            .Where(workflow => workflow.IsActive)
            .Select(workflow => new { workflow.Id, workflow.Definition })
            .ToListAsync();

        var count = 0;
        foreach (var workflow in activeWebhookWorkflows)
        {
            if (!ContainsWebhookTrigger(workflow.Definition.RootElement))
            {
                continue;
            }

            await registrar.SyncAsync(workflow.Id, workflow.Definition.RootElement, isActive: true);
            count++;
        }

        if (count > 0)
        {
            logger.LogInformation("{Count} aktif webhook workflow kaydi workflow-key semasina senkronlandi.", count);
        }
    }

    private static bool ContainsWebhookTrigger(System.Text.Json.JsonElement definition)
    {
        if (!definition.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return false;
        }

        foreach (var node in nodes.EnumerateArray())
        {
            if (node.TryGetProperty("type", out var type) &&
                string.Equals(type.GetString(), "webhook.trigger", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
