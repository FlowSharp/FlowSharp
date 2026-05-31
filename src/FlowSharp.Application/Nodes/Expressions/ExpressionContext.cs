using System.Text.Json.Nodes;

namespace FlowSharp.Application.Nodes.Expressions;

/// <summary>
/// Bir ifade cozumlenirken gerekli calisma-zamani verisi: mevcut item, item indeksi,
/// daha once calismis node'larin ciktilari ve trigger payload'i.
/// </summary>
public sealed class ExpressionContext
{
    public NodeItem? CurrentItem { get; init; }

    public int ItemIndex { get; init; }

    public int RunIndex { get; init; }

    /// <summary>Node ornek adi -> o node'un cikis item'lari (ilk cikis portu).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<NodeItem>> NodeOutputs { get; init; }
        = new Dictionary<string, IReadOnlyList<NodeItem>>(StringComparer.OrdinalIgnoreCase);

    public JsonObject? Trigger { get; init; }
}
