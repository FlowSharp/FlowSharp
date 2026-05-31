namespace FlowSharp.Web.Localization;

/// <summary>Desteklenen bir dil (lang/ klasoründeki bir JSON dosyasi).</summary>
public sealed record CultureOption(string Code, string DisplayName);

/// <summary>
/// lang/ klasoründeki JSON dosyalarindan (anahtar -> ceviri) cevirileri saglar. Yeni bir dil
/// eklemek icin lang/&lt;code&gt;.json dosyasi birakmak yeterlidir (orn. lang/de.json).
/// Node'lar icin anahtar kalibi: <c>node.&lt;key&gt;.displayName</c> / <c>.description</c>.
/// </summary>
public interface ILocalizer
{
    /// <summary>Anahtari mevcut UI diline gore cevirir; bulunamazsa anahtari aynen doner.</summary>
    string this[string key] { get; }

    /// <summary>lang/ klasoründen bulunan desteklenen diller.</summary>
    IReadOnlyList<CultureOption> SupportedCultures { get; }
}
