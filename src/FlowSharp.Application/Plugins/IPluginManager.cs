namespace FlowSharp.Application.Plugins;

/// <summary>Bir plugin icindeki tek bir node ozeti (UI listesi icin).</summary>
public sealed record PluginNodeInfo(string Key, string DisplayName, string Category);

/// <summary>Yuklenmis (veya yuklenememis) bir plugin'in durumu.</summary>
public sealed record PluginInfo(
    string Name,
    bool Loaded,
    string? Error,
    IReadOnlyList<PluginNodeInfo> Nodes,
    DateTimeOffset? LoadedAt);

/// <summary>
/// "plugins/" klasorundeki topluluk plugin'lerini yonetir: her plugin bir alt klasordur,
/// icindeki tum .cs kaynak dosyalari Roslyn ile calisma zamaninda derlenip yuklenir ve
/// icindeki <c>INodeType</c>'lar node kaydina eklenir. Admin marketplace'ten kurar/kaldirir.
/// </summary>
public interface IPluginManager
{
    /// <summary>Config'ten gelen resmi/onerilen marketplace deposu adresi (yoksa null).</summary>
    string? OfficialMarketplaceUrl { get; }

    /// <summary>Bilinen tum plugin'lerin guncel durumu.</summary>
    IReadOnlyList<PluginInfo> List();

    /// <summary>Uygulama acilisinda plugins klasorundeki tum plugin'leri derleyip yukler.</summary>
    Task LoadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Tek bir plugin'i yeniden derleyip yukler (kod degisince).</summary>
    Task<PluginInfo> ReloadAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Plugin'i kayittan cikarir ve klasorunu siler.</summary>
    Task RemoveAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Bir GitHub deposundan plugin indirir, plugins klasorune yazar ve yukler.</summary>
    Task<PluginInfo> InstallFromGitHubAsync(string repoUrl, CancellationToken cancellationToken = default);
}
