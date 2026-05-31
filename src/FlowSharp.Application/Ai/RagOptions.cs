namespace FlowSharp.Application.Ai;

/// <summary>appsettings.json "Rag" bolumu. Embedding tamamen yereldir (gomulu model), ek ayar gerekmez.</summary>
public sealed class RagOptions
{
    public const string SectionName = "Rag";

    /// <summary>
    /// Vektor veritabanlarinin tutuldugu klasor (ContentRoot'a gore veya tam yol). Her workspace
    /// (workflow) icin ayri bir SQLite dosyasi olusturulur; boylece RAG verisi izole kalir.
    /// </summary>
    public string DatabaseDirectory { get; set; } = "App_Data/rag";
}
