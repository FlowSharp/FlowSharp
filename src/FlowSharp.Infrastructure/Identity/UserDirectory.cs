using Microsoft.EntityFrameworkCore;
using FlowSharp.Application.Identity;
using FlowSharp.Infrastructure.Data;

namespace FlowSharp.Infrastructure.Identity;

/// <inheritdoc cref="IUserDirectory"/>
public sealed class UserDirectory(ApplicationDbContext dbContext) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<string, string>> GetEmailsAsync(
        IEnumerable<string> userIds, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        return await dbContext.Users
            .Where(user => ids.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.Email ?? user.UserName ?? user.Id, cancellationToken);
    }
}
