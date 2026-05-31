using System.Text.Json.Nodes;

namespace FlowSharp.Application.Nodes;

/// <summary>
/// Bir node calistirilirken motorun node'a sagladigi baglam.
/// <c>IExecuteFunctions</c> karsiligi: giris item'larina, parametre okuma
/// (expression cozumlemeli) yardimcilarina ve servislere erisim verir.
/// </summary>
public interface INodeExecutionContext
{
    /// <summary>Node'un tip anahtari (orn. "http.request").</summary>
    string NodeKey { get; }

    /// <summary>Workflow icindeki node ornek adi (orn. "HTTP Request").</summary>
    string NodeName { get; }

    /// <summary>Bu node'a gelen giris item'lari (tum gelen baglantilarin birlesimi).</summary>
    IReadOnlyList<NodeItem> Items { get; }

    /// <summary>DI servis saglayicisi (HttpClient, DbContext, options vb. icin).</summary>
    IServiceProvider Services { get; }

    CancellationToken CancellationToken { get; }

    /// <summary>Bu calismayi baslatan trigger payload'i (orn. webhook gövdesi, alt-workflow derinligi).</summary>
    JsonObject? Trigger { get; }

    /// <summary>Calisan workflow'un kimligi (workspace'e ozel kaynaklari izole etmek icin; null olabilir).</summary>
    Guid? WorkflowId { get; }

    /// <summary>Ham parametre dugumunu dondurur (expression cozumlemeden).</summary>
    JsonNode? GetRawParameter(string name);

    /// <summary>
    /// Parametreyi okur ve icindeki <c>{{ ... }}</c> ifadelerini verilen item baglaminda cozer.
    /// itemIndex, $json/$item gibi ifadelerin hangi giris item'ina baktigini belirler.
    /// </summary>
    string? GetString(string name, int itemIndex = 0, string? defaultValue = null);

    bool GetBoolean(string name, int itemIndex = 0, bool defaultValue = false);

    double GetNumber(string name, int itemIndex = 0, double defaultValue = 0);

    int GetInt(string name, int itemIndex = 0, int defaultValue = 0);

    /// <summary>Parametreyi JSON olarak okur ve expression'lari cozer.</summary>
    JsonNode? GetJson(string name, int itemIndex = 0);

    /// <summary>
    /// Verilen JSON yapisi icindeki tum string degerlerde gecen <c>{{ ... }}</c> ifadelerini
    /// ozyinelemeli olarak cozer (obje/dizi/deger). Set, HTTP body gibi ic ice degerler icindir.
    /// </summary>
    JsonNode? ResolveValue(JsonNode? value, int itemIndex = 0);

    /// <summary>
    /// Sifreli credential store'dan bir credential alanini cozer. type credential tip anahtari
    /// (orn. "openAiApi"), name kullanicinin sectigi credential adi, field istenen alandir (orn. "apiKey").
    /// </summary>
    Task<string?> GetCredentialAsync(string type, string name, string field);

    /// <summary>Calisma sirasinda bilgi/uyari kaydi birakir (execution log'una yansir).</summary>
    void Log(string message);
}
