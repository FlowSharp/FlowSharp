namespace FlowSharp.Application.Plugins;

/// <summary>
/// appsettings.json "Plugins" bolumunden baglanan plugin ayarlari. Resmi marketplace
/// adresi ve plugin klasoru tek yerden (config) yonetilir.
/// </summary>
public sealed class PluginOptions
{
    public const string SectionName = "Plugins";

    /// <summary>Pluginlerin saklandigi klasor (ContentRoot'a gore). Varsayilan: "plugins".</summary>
    public string Path { get; set; } = "plugins";

    /// <summary>Resmi/onerilen plugin deposunun GitHub adresi (UI'da gosterilir/onerilir).</summary>
    public string? OfficialMarketplaceUrl { get; set; }
}
