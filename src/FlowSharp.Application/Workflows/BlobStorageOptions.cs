namespace FlowSharp.Application.Workflows;

/// <summary>
/// appsettings.json "BlobStorage" bolumu. Buyuk execution ciktilarinin DB yerine harici depoya
/// (offload) tasinmasini yonetir. Kapaliyken (varsayilan) davranis degismez; tum cikti DB'de kalir.
/// </summary>
public sealed class BlobStorageOptions
{
    public const string SectionName = "BlobStorage";

    /// <summary>Offload etkin mi? Kapaliyken hicbir cikti disari tasinmaz.</summary>
    public bool Enabled { get; set; }

    /// <summary>Dosya sistemi saglayicisinin kok dizini.</summary>
    public string Directory { get; set; } = "App_Data/blobs";

    /// <summary>Bu boyutu (byte) asan execution ciktilari blob deposuna tasinir. Varsayilan 64 KB.</summary>
    public int ThresholdBytes { get; set; } = 65536;
}
