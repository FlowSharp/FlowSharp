using FlowSharp.Application.Credentials;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Credentials;

namespace FlowSharp.Nodes.Credentials;

/// <summary>
/// Tum credential semalarinin merkez kaydi. Sema TUTMAZ; sadece node'lardan
/// (<see cref="IProvidesCredentials"/>) ve bagimsiz credential tiplerinden
/// (<see cref="ICredentialType"/>) kesfederek toplar. Yeni bir credential tipi eklemek
/// icin onu kullanan node'a sema bildirmek yeterlidir; burada degisiklik gerekmez.
/// </summary>
public sealed class CredentialCatalog : ICredentialCatalog
{
    private readonly Dictionary<string, CredentialSchema> schemas;

    public CredentialCatalog(IEnumerable<INodeType> nodes, IEnumerable<ICredentialType> pluginTypes)
    {
        schemas = new Dictionary<string, CredentialSchema>(StringComparer.OrdinalIgnoreCase);

        foreach (var schema in nodes.OfType<IProvidesCredentials>().SelectMany(node => node.CredentialSchemas))
        {
            schemas[schema.Type] = schema;
        }

        // Node'a bagli olmayan bagimsiz credential tipleri (varsa) uzerine eklenir.
        foreach (var type in pluginTypes)
        {
            schemas[type.Schema.Type] = type.Schema;
        }
    }

    public IReadOnlyList<CredentialSchema> GetAll() => schemas.Values.ToArray();

    public CredentialSchema? Find(string type) =>
        type is not null && schemas.TryGetValue(type, out var schema) ? schema : null;
}
