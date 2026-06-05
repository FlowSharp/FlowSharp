using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes;

/// <summary>
/// DI tarafindan kesfedilen tum <see cref="INodeType"/> ornekleriyle olusan merkez kayit.
/// Palette yalniz gercekten calisan node'lari gosterir; yeni node eklemek icin tek bir
/// <see cref="INodeType"/> sinifi yazmak yeterlidir (otomatik kesfedilir). Plugin yukleyici
/// calisma zamaninda <see cref="Register"/>/<see cref="Unregister"/> ile node ekleyip cikarabilir.
/// </summary>
public sealed class NodeRegistry : INodeRegistry
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, INodeType> executables;
    private IReadOnlyList<NodeDefinition> definitions;

    public NodeRegistry(IEnumerable<INodeType> nodeTypes)
    {
        executables = new Dictionary<string, INodeType>(StringComparer.OrdinalIgnoreCase);
        foreach (var nodeType in nodeTypes)
        {
            executables[nodeType.Definition.Key] = nodeType;
        }

        definitions = BuildDefinitions();
    }

    public IReadOnlyList<NodeDefinition> Definitions
    {
        get
        {
            lock (gate)
            {
                return definitions;
            }
        }
    }

    public INodeType? Find(string key)
    {
        lock (gate)
        {
            return executables.TryGetValue(key, out var nodeType) ? nodeType : null;
        }
    }

    public bool IsExecutable(string key)
    {
        lock (gate)
        {
            return executables.ContainsKey(key);
        }
    }

    public void Register(INodeType node)
    {
        lock (gate)
        {
            executables[node.Definition.Key] = node;
            definitions = BuildDefinitions();
        }
    }

    public bool Unregister(string key)
    {
        lock (gate)
        {
            var removed = executables.Remove(key);
            if (removed)
            {
                definitions = BuildDefinitions();
            }

            return removed;
        }
    }

    private IReadOnlyList<NodeDefinition> BuildDefinitions() =>
        executables.Values
            .Select(nodeType => nodeType.Definition)
            .OrderBy(definition => definition.CategoryKey)
            .ThenBy(definition => definition.SubCategoryKey)
            .ThenBy(definition => definition.SortOrder)
            .ThenBy(definition => definition.DisplayName)
            .ToArray();
}
