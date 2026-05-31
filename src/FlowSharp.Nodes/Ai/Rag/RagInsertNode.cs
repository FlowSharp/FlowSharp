using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using FlowSharp.Application.Ai;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Ai.Rag;

/// <summary>
/// Gelen item'lardaki metni embedding'e cevirip SQLite vektor deposuna yazar (RAG indeksleme).
/// Ornek: Spreadsheet'ten gelen urunleri "products" koleksiyonuna gomer.
/// </summary>
public sealed class RagInsertNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "rag.insert",
        DisplayName: "Vector Store: Insert",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Action,
        Description: "Item metinlerini embedding'leyip vektor deposuna ekler (RAG).",
        Parameters:
        [
            new NodeParameterDefinition("collection", "Koleksiyon", NodeParameterType.String, IsRequired: true,
                DefaultValue: "default", HelpText: "Mantiksal grup adi (orn. products)."),
            new NodeParameterDefinition("textField", "Metin alani", NodeParameterType.String, DefaultValue: "text",
                HelpText: "Embedlenecek alan; yoksa tum item JSON'i kullanilir."),
            new NodeParameterDefinition("idField", "ID alani", NodeParameterType.String,
                HelpText: "Bos ise otomatik GUID uretilir."),
            new NodeParameterDefinition("metadataField", "Metadata alani", NodeParameterType.String),
            new NodeParameterDefinition("clearFirst", "Once temizle?", NodeParameterType.Boolean, DefaultValue: "false",
                HelpText: "true ise koleksiyon eklemeden once bosaltilir.")
        ],
        Tags: ["ai", "rag", "vector"],
        Icon: "database",
        Color: "#10a37f");

    public override async Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var embedder = context.Services.GetRequiredService<IEmbeddingGenerator>();
        var store = context.Services.GetRequiredService<IVectorStore>();

        // Workspace izolasyonu: her workflow kendi vektor deposunu kullanir.
        var scope = context.WorkflowId?.ToString("N") ?? "global";
        var collection = context.GetString("collection") ?? "default";
        var textField = context.GetString("textField") ?? "text";
        var idField = context.GetString("idField");
        var metadataField = context.GetString("metadataField");

        if (context.Items.Count == 0)
        {
            return NodeExecutionResult.Single(NodeItem.From(new JsonObject { ["inserted"] = 0 }));
        }

        var texts = context.Items
            .Select(item => item.Json.TryGetPropertyValue(textField, out var v) && v is not null
                ? v.ToString()
                : item.Json.ToJsonString())
            .ToList();

        var vectors = await embedder.EmbedAsync(texts, context.CancellationToken);

        var records = new List<VectorRecord>(context.Items.Count);
        for (var i = 0; i < context.Items.Count; i++)
        {
            var json = context.Items[i].Json;
            var id = !string.IsNullOrWhiteSpace(idField) && json.TryGetPropertyValue(idField, out var idv) && idv is not null
                ? idv.ToString()
                : Guid.NewGuid().ToString("N");
            var metadata = !string.IsNullOrWhiteSpace(metadataField) && json.TryGetPropertyValue(metadataField, out var mv) && mv is not null
                ? mv.ToString()
                : json.ToJsonString();

            records.Add(new VectorRecord(id, texts[i], vectors[i], metadata));
        }

        if (context.GetBoolean("clearFirst"))
        {
            await store.ClearAsync(scope, collection, context.CancellationToken);
        }

        await store.UpsertAsync(scope, collection, records, context.CancellationToken);

        return NodeExecutionResult.Single(NodeItem.From(new JsonObject
        {
            ["collection"] = collection,
            ["inserted"] = records.Count
        }));
    }
}
