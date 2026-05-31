namespace FlowSharp.Application.Ai;

/// <summary>Vektor deposuna yazilan bir kayit.</summary>
public sealed record VectorRecord(string Id, string Text, float[] Vector, string? Metadata);

/// <summary>Arama sonucu: benzerlik skoruyla bir kayit.</summary>
public sealed record VectorMatch(string Id, string Text, string? Metadata, float Score);

/// <summary>
/// Basit vektor deposu (RAG icin). <paramref name="scope"/> bir workspace/workflow'u izole eder
/// (her scope kendi SQLite dosyasinda). <c>collection</c> ise scope icindeki mantiksal gruptur.
/// Varsayilan implementasyon kosinus benzerligiyle arar.
/// </summary>
public interface IVectorStore
{
    Task UpsertAsync(string scope, string collection, IReadOnlyList<VectorRecord> records, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VectorMatch>> SearchAsync(string scope, string collection, float[] query, int topK, CancellationToken cancellationToken = default);

    Task ClearAsync(string scope, string collection, CancellationToken cancellationToken = default);
}
