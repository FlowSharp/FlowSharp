namespace FlowSharp.Application.Abstractions;

/// <summary>Sifreli credential'larin yonetimi (CRUD) ve node'lar icin cozumlemesi.</summary>
public interface ICredentialStore
{
    Task<IReadOnlyList<CredentialSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<CredentialDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Olusturur veya gunceller (Id null ise yeni). Hassas alanlar sifrelenir.</summary>
    Task<Guid> SaveAsync(CredentialInput input, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Bir node icin credential alanlarini cozer (sifre cozulmus). Bulunamazsa null.</summary>
    Task<IReadOnlyDictionary<string, string>?> ResolveAsync(string type, string name, CancellationToken cancellationToken = default);
}

public sealed record CredentialSummary(Guid Id, string Name, string Type, DateTimeOffset CreatedAt);

public sealed record CredentialDetail(Guid Id, string Name, string Type, IReadOnlyDictionary<string, string> Data);

public sealed record CredentialInput(Guid? Id, string Name, string Type, IReadOnlyDictionary<string, string> Data);
