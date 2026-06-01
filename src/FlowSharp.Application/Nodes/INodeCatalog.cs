using FlowSharp.Domain.Nodes;

namespace FlowSharp.Application.Nodes;

public interface INodeCatalog
{
    IReadOnlyList<NodeDefinition> GetAll();

    IReadOnlyList<NodeDefinition> GetByCategory(NodeCategory category);

    IReadOnlyList<NodeDefinition> GetByCategory(string category);

    NodeDefinition? Find(string key);
}
