namespace FlowSharp.Application.Ai;

/// <summary>
/// Metinleri vektore (embedding) cevirir. Varsayilan implementasyon yerel Ollama'yi kullanir;
/// arayuz sayesinde ileride OpenAI vb. ile degistirilebilir.
/// </summary>
public interface IEmbeddingGenerator
{
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}
