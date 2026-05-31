using Microsoft.AspNetCore.Identity;

namespace FlowSharp.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
}
