using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using FlowSharp.Application.Security;
using FlowSharp.Infrastructure.Identity;

namespace FlowSharp.Web.Security;

/// <summary>
/// Oturum acmis kullanicinin kimligini ve Admin olup olmadigini cozer. Sahiplik (owner)
/// filtreleri icin kullanilir: Admin tum kayitlari gorur, digerleri yalniz kendi OwnerId'sini.
/// </summary>
internal static class CurrentUser
{
    public static async Task<(string? Id, bool IsAdmin)> ResolveAsync(AuthenticationStateProvider provider)
    {
        var user = (await provider.GetAuthenticationStateAsync()).User;
        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return (id, user.IsInRole(IdentitySeeder.AdminRole));
    }

    /// <summary>Oturum sahibini, is mantigi servislerine gecirilecek <see cref="ActorScope"/> olarak cozer.</summary>
    public static async Task<ActorScope> ResolveScopeAsync(AuthenticationStateProvider provider)
    {
        var (id, isAdmin) = await ResolveAsync(provider);
        return new ActorScope(id, isAdmin);
    }
}
