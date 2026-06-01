using System.Globalization;
using FlowSharp.Application.Localization;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes;

/// <summary>
/// UI/palette icin node tanimlarini <see cref="INodeRegistry"/> uzerinden sunar. Sunarken
/// node tanimini mevcut UI diline gore yerellestirir (DisplayName/Description); boylece tum
/// tuketiciler (palette, canvas, NDV) otomatik olarak cevrilmis ad alir. Ceviri yoksa tanimdaki
/// degerler korunur.
/// </summary>
public sealed class NodeCatalog(INodeRegistry registry, INodeTranslationStore translations) : INodeCatalog
{
    public IReadOnlyList<NodeDefinition> GetAll() =>
        registry.Definitions.Select(Localize).ToArray();

    public IReadOnlyList<NodeDefinition> GetByCategory(NodeCategory category) =>
        registry.Definitions.Where(node => node.Category == category).Select(Localize).ToArray();

    public IReadOnlyList<NodeDefinition> GetByCategory(string category) =>
        registry.Definitions
            .Where(node => node.CategoryKey.Equals(category, StringComparison.OrdinalIgnoreCase))
            .Select(Localize)
            .ToArray();

    public NodeDefinition? Find(string key)
    {
        var def = registry.Definitions.FirstOrDefault(node => node.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        return def is null ? null : Localize(def);
    }

    private NodeDefinition Localize(NodeDefinition def)
    {
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var name = translations.Get(culture, $"{def.Key}.displayName");
        var description = translations.Get(culture, $"{def.Key}.description");

        return name is null && description is null
            ? def
            : def with { DisplayName = name ?? def.DisplayName, Description = description ?? def.Description };
    }
}
