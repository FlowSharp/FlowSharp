using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FlowSharp.Application.Abstractions;
using FlowSharp.Domain.Credentials;
using FlowSharp.Infrastructure.Data;
using FlowSharp.Infrastructure.Security;

namespace FlowSharp.Infrastructure.Credentials;

public sealed class CredentialStore(ApplicationDbContext dbContext, ICredentialProtector protector) : ICredentialStore
{
    public async Task<IReadOnlyList<CredentialSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Credentials
            .OrderBy(credential => credential.Name)
            .Select(credential => new CredentialSummary(credential.Id, credential.Name, credential.Type, credential.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<CredentialDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var credential = await dbContext.Credentials.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        return credential is null ? null : new CredentialDetail(credential.Id, credential.Name, credential.Type, Decrypt(credential));
    }

    public async Task<Guid> SaveAsync(CredentialInput input, CancellationToken cancellationToken = default)
    {
        var encrypted = protector.Encrypt(JsonSerializer.Serialize(input.Data));

        Credential credential;
        if (input.Id is { } id && await dbContext.Credentials.FirstOrDefaultAsync(item => item.Id == id, cancellationToken) is { } existing)
        {
            existing.Name = input.Name;
            existing.Type = input.Type;
            existing.EncryptedData = encrypted;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            credential = existing;
        }
        else
        {
            credential = new Credential { Name = input.Name, Type = input.Type, EncryptedData = encrypted };
            dbContext.Credentials.Add(credential);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return credential.Id;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await dbContext.Credentials.Where(credential => credential.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, string>?> ResolveAsync(string type, string name, CancellationToken cancellationToken = default)
    {
        var credential = await dbContext.Credentials
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Type == type && item.Name == name, cancellationToken);

        return credential is null ? null : Decrypt(credential);
    }

    private IReadOnlyDictionary<string, string> Decrypt(Credential credential)
    {
        try
        {
            var json = protector.Decrypt(credential.EncryptedData);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}
