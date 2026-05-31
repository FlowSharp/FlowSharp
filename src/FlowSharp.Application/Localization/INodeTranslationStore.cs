namespace FlowSharp.Application.Localization;

/// <summary>
/// Node cevirilerinin merkezi deposu. Node'lar bagimsiz uretildigi icin cevirileri de kendi
/// kaynaginda gelir: built-in node'lar lang/nodes/&lt;culture&gt;.json, plugin'ler ise
/// plugins/&lt;plugin&gt;/lang/&lt;culture&gt;.json. Her kaynak bir "source" anahtariyla eklenir;
/// plugin kaldirilinca yalniz kendi cevirileri temizlenir. Anahtar kalibi:
/// <c>&lt;nodeKey&gt;.displayName</c> / <c>&lt;nodeKey&gt;.description</c>.
/// </summary>
public interface INodeTranslationStore
{
    /// <summary>Bir kaynagin (built-in veya plugin) belirli dildeki cevirilerini ekler/gunceller.</summary>
    void Set(string source, string culture, IReadOnlyDictionary<string, string> entries);

    /// <summary>Bir kaynagin tum cevirilerini (tum diller) kaldirir (plugin kaldirilinca).</summary>
    void Remove(string source);

    /// <summary>Anahtari verilen dile gore cozer; bulunamazsa varsayilan dile, o da yoksa null.</summary>
    string? Get(string culture, string key);
}
