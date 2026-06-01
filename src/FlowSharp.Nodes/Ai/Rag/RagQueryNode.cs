using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using FlowSharp.Application.Ai;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Ai.Rag;

/// <summary>
/// Bir soruyu embedding'leyip vektor deposunda en yakin K kaydi bulur (RAG getirme).
/// Her eslesme bir item olur: text, score, metadata, id. Cikti AI Agent'a baglam olarak verilebilir.
/// </summary>
public sealed class RagQueryNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "rag.query",
        DisplayName: "Vector Store: Query",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Action,
        Description: "Vektor deposunda anlamsal arama yapar (RAG).",
        Parameters:
        [
            new NodeParameterDefinition("collection", "Koleksiyon", NodeParameterType.String, IsRequired: true,
                DefaultValue: "default"),
            new NodeParameterDefinition("query", "Sorgu", NodeParameterType.String, IsRequired: true,
                HelpText: "Aranacak metin. Ornek: {{$json.question}}"),
            new NodeParameterDefinition("topK", "Sonuc sayisi", NodeParameterType.Number, DefaultValue: "4")
        ],
        Tags: ["ai", "rag", "vector"],
        Icon: "database",
        Color: "#10a37f",
        SubCategory: "AI Memory");

    public override async Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var embedder = context.Services.GetRequiredService<IEmbeddingGenerator>();
        var store = context.Services.GetRequiredService<IVectorStore>();

        var scope = context.WorkflowId?.ToString("N") ?? "global";
        var collection = context.GetString("collection") ?? "default";
        var query = context.GetString("query") ?? "";
        var topK = Math.Max(1, context.GetInt("topK", defaultValue: 4));

        if (string.IsNullOrWhiteSpace(query))
        {
            return NodeExecutionResult.Failure("Sorgu metni gerekli.");
        }

        var vectors = await embedder.EmbedAsync([query], context.CancellationToken);
        var matches = await store.SearchAsync(scope, collection, vectors[0], topK, context.CancellationToken);

        var items = matches.Select(match => NodeItem.From(new JsonObject
        {
            ["id"] = match.Id,
            ["text"] = match.Text,
            ["score"] = match.Score,
            ["metadata"] = match.Metadata
        })).ToList();

        return NodeExecutionResult.Single(items);
    }
}

/// <summary>
/// AI Agent'in Memory portuna baglanan RAG hafizasi. Agent calisirken kullanici girdisini
/// vektor deposunda arar ve bulunan metinleri model prompt'una baglam olarak ekler.
/// </summary>
public sealed class RagMemoryNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "rag.memory",
        DisplayName: "Vector Store: Memory",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Ai,
        Description: "AI Agent icin vektor deposundan ilgili baglam getirir.",
        Parameters:
        [
            new NodeParameterDefinition("collection", "Koleksiyon", NodeParameterType.String, IsRequired: true,
                DefaultValue: "default"),
            new NodeParameterDefinition("query", "Sorgu", NodeParameterType.String, IsRequired: true,
                DefaultValue: "{{$json.input}}",
                HelpText: "Aranacak metin. Agent girdisi icin varsayilan: {{$json.input}}"),
            new NodeParameterDefinition("topK", "Sonuc sayisi", NodeParameterType.Number, DefaultValue: "4")
        ],
        Tags: ["ai", "rag", "memory", "vector"],
        Icon: "database",
        Color: "#10a37f",
        SubCategory: "AI Memory",
        Inputs: [],
        Outputs: [new NodePort("memory", "Memory", NodePortType.AiMemory)]);

    public override async Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var embedder = context.Services.GetRequiredService<IEmbeddingGenerator>();
        var store = context.Services.GetRequiredService<IVectorStore>();

        var scope = context.WorkflowId?.ToString("N") ?? "global";
        var collection = context.GetString("collection") ?? "default";
        var query = context.GetString("query") ?? "";
        var topK = Math.Max(1, context.GetInt("topK", defaultValue: 4));

        if (string.IsNullOrWhiteSpace(query))
        {
            return NodeExecutionResult.Single([]);
        }

        var vectors = await embedder.EmbedAsync([query], context.CancellationToken);
        var matches = await store.SearchAsync(scope, collection, vectors[0], topK, context.CancellationToken);

        var items = matches.Select(match => NodeItem.From(new JsonObject
        {
            ["id"] = match.Id,
            ["text"] = match.Text,
            ["score"] = match.Score,
            ["metadata"] = match.Metadata
        })).ToList();

        return NodeExecutionResult.Single(items);
    }
}
