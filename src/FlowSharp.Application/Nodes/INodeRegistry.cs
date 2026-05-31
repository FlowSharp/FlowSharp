using FlowSharp.Domain.Nodes;

namespace FlowSharp.Application.Nodes;

/// <summary>
/// Tum kayitli node tiplerinin merkezi kaydidir. DI assembly taramasiyla bulunan
/// <see cref="INodeType"/> ornekleri buraya yuklenir; ayrica henuz kodlanmamis
/// fakat palette gorunmesi istenen bildirimsel (placeholder) tanimlari da birlestirir.
/// </summary>
public interface INodeRegistry
{
    /// <summary>Verilen anahtara karsilik gelen calistirilabilir node; yoksa null.</summary>
    INodeType? Find(string key);

    /// <summary>Anahtarin calistirilabilir bir implementasyonu var mi?</summary>
    bool IsExecutable(string key);

    /// <summary>Palette/katalog icin tum node tanimlari (implemented + placeholder).</summary>
    IReadOnlyList<NodeDefinition> Definitions { get; }

    /// <summary>
    /// Calisma zamaninda bir node tipini kaydeder (plugin yukleme icin). Ayni anahtar varsa uzerine yazar.
    /// </summary>
    void Register(INodeType node);

    /// <summary>Calisma zamaninda bir node tipini kayittan cikarir; cikarildiysa true.</summary>
    bool Unregister(string key);
}
