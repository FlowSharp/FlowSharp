using FlowSharp.Domain.Credentials;

namespace FlowSharp.Application.Credentials;

/// <summary>
/// Bir credential tipini (semasini) saglar. DI tarafindan otomatik kesfedilir.
/// Bir node'a bagli olmayan, bagimsiz credential tipleri (orn. saf plugin'ler) icindir.
/// Node'lar genelde <see cref="IProvidesCredentials"/> kullanir.
/// </summary>
public interface ICredentialType
{
    CredentialSchema Schema { get; }
}

/// <summary>
/// Bir node, kullandigi credential tip(ler)inin semasini DOGRUDAN kendisi bildirir.
/// Boylece sema merkezi bir listede degil, onu kullanan node'un yaninda yasar (tam dinamik).
/// </summary>
public interface IProvidesCredentials
{
    IEnumerable<CredentialSchema> CredentialSchemas { get; }
}

/// <summary>Tum credential semalarinin merkez kaydi. UI ve dogrulama buradan okur.</summary>
public interface ICredentialCatalog
{
    IReadOnlyList<CredentialSchema> GetAll();

    CredentialSchema? Find(string type);
}
