using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FlowSharp.Application.Json;

/// <summary>
/// Tum projede JSON ile ilgili tek merkez. Cikti/metin uretirken Turkce dahil hicbir dilin
/// karakterleri <c>\uXXXX</c>'e kacirilmaz (UTF-8 oldugu gibi yazilir). "Relaxed" encoder
/// HTML-duyarli karakterleri de kacirmaz; bu yuzden ciktiyi HTML icine gomerken degil, API
/// govdesi/cevabi gibi JSON baglamlarinda kullanilir (bizim tum kullanimimiz boyle).
/// </summary>
public static class FlowJson
{
    /// <summary>Kompakt cikti (HTTP yaniti, dis API govdesi, ifade cozumu) icin.</summary>
    public static readonly JsonSerializerOptions Relaxed = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Okunabilir (girintili) cikti; editor onizlemesi gibi insan tarafindan okunan yerler icin.</summary>
    public static readonly JsonSerializerOptions RelaxedIndented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Gecerli JSON'u okunabilir (girintili) hale getirir; gecersiz JSON'a DOKUNMAZ (kullanici
    /// verisini bozmaz). Ifade iceren ama yine de gecerli JSON olan degerler de
    /// (orn. <c>{"to":"{{$json.from}}"}</c>) sorunsuz bicimlenir.
    /// </summary>
    public static string Beautify(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw ?? string.Empty;
        }

        try
        {
            return JsonNode.Parse(raw)?.ToJsonString(RelaxedIndented) ?? raw;
        }
        catch (JsonException)
        {
            return raw; // Gecersiz JSON: oldugu gibi birak.
        }
    }
}
