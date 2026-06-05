namespace FlowSharp.Application.Abstractions;

/// <summary>
/// Buyuk metin (genelde JSON) iceriklerini veritabani disinda saklayan basit anahtar-deger deposu.
/// Execution ciktilari belli bir esigi astiginda DB sismesini onlemek icin buraya tasinir (offload).
/// Varsayilan implementasyon dosya sistemidir; ayni arayuzle S3/Azure ileride eklenebilir.
/// </summary>
public interface IBlobStore
{
    /// <summary>Icerigi saklar ve sonradan erisim icin bir referans (anahtar) doner.</summary>
    Task<string> SaveAsync(string content, CancellationToken cancellationToken = default);

    /// <summary>Referansa karsilik gelen icerigi doner; bulunamazsa <c>null</c>.</summary>
    Task<string?> GetAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Referansa karsilik gelen icerigi siler (yoksa sessizce gecer).</summary>
    Task DeleteAsync(string reference, CancellationToken cancellationToken = default);
}
