using FlowSharp.Application.Ai;
using SmartComponents.LocalEmbeddings;

namespace FlowSharp.Nodes.Ai.Embeddings;

/// <summary>
/// Tamamen yerel, in-process embedding uretici (SmartComponents.LocalEmbeddings — gomulu ONNX
/// modeli). Harici servis/Ollama gerektirmez; ilk kullanimda model bir kez yuklenir.
/// </summary>
public sealed class LocalEmbeddingGenerator : IEmbeddingGenerator, IDisposable
{
    private readonly LocalEmbedder embedder = new();
    private readonly object gate = new();

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        var result = new List<float[]>(texts.Count);
        lock (gate)
        {
            foreach (var text in texts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var embedding = embedder.Embed(text ?? string.Empty);
                result.Add(embedding.Values.ToArray());
            }
        }

        return Task.FromResult<IReadOnlyList<float[]>>(result);
    }

    public void Dispose() => embedder.Dispose();
}
