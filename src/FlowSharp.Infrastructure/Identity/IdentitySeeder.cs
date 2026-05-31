using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using FlowSharp.Domain.Security;

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

    private static readonly Dictionary<string, string[]> RolePermissions = new()
    {
        [AdminRole] = AppPermissions.All,
        [EditorRole] = [AppPermissions.WorkflowsRead, AppPermissions.WorkflowsWrite, AppPermissions.WorkflowsExecute, AppPermissions.ExecutionsRead],
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
    }
}
