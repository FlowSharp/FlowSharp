using System.Globalization;
using System.Text.Json.Nodes;
using FlowSharp.Application.Abstractions;
using FlowSharp.Application.Json;
using FlowSharp.Application.Nodes;
using FlowSharp.Application.Nodes.Expressions;

namespace FlowSharp.Infrastructure.Workflows;

/// <summary>
/// <see cref="INodeExecutionContext"/>'in motor tarafindan node basina olusturulan somut hali.
/// Parametre okurken degerleri verilen item baglaminda expression cozumler.
/// </summary>
internal sealed class NodeExecutionContext(
    string nodeKey,
    string nodeName,
    JsonObject parameters,
    IReadOnlyList<NodeItem> items,
    IReadOnlyDictionary<string, IReadOnlyList<NodeItem>> nodeOutputs,
    JsonObject? trigger,
    int runIndex,
    IExpressionEvaluator evaluator,
    IServiceProvider services,
    Action<string> logSink,
    CancellationToken cancellationToken,
    Guid? workflowId = null,
    string? actorOwnerId = null) : INodeExecutionContext
{
    public string NodeKey => nodeKey;

    public string NodeName => nodeName;

    public IReadOnlyList<NodeItem> Items => items;

    public IServiceProvider Services => services;

    public CancellationToken CancellationToken => cancellationToken;

    public JsonObject? Trigger => trigger;

    public Guid? WorkflowId => workflowId;

    public JsonNode? GetRawParameter(string name) =>
        parameters.TryGetPropertyValue(name, out var value) ? value : null;

    public string? GetString(string name, int itemIndex = 0, string? defaultValue = null)
    {
        var raw = GetRawParameter(name);
        if (raw is null)
        {
            return defaultValue;
        }

        if (raw is JsonValue value && value.TryGetValue<string>(out var text))
        {
            var resolved = evaluator.EvaluateToString(text, BuildContext(itemIndex));
            return string.IsNullOrEmpty(resolved) ? defaultValue : resolved;
        }

        return raw.ToJsonString(FlowJson.Relaxed);
    }

    public bool GetBoolean(string name, int itemIndex = 0, bool defaultValue = false)
    {
        var node = GetJson(name, itemIndex);
        return node switch
        {
            JsonValue v when v.TryGetValue<bool>(out var b) => b,
            JsonValue v when v.TryGetValue<string>(out var s) && bool.TryParse(s, out var parsed) => parsed,
            _ => defaultValue
        };
    }

    public double GetNumber(string name, int itemIndex = 0, double defaultValue = 0)
    {
        var node = GetJson(name, itemIndex);
        return node switch
        {
            JsonValue v when v.TryGetValue<double>(out var d) => d,
            JsonValue v when v.TryGetValue<string>(out var s) &&
                double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => defaultValue
        };
    }

    public int GetInt(string name, int itemIndex = 0, int defaultValue = 0) =>
        (int)GetNumber(name, itemIndex, defaultValue);

    public JsonNode? GetJson(string name, int itemIndex = 0)
    {
        var raw = GetRawParameter(name);
        if (raw is null)
        {
            return null;
        }

        if (raw is JsonValue value && value.TryGetValue<string>(out var text) && evaluator.ContainsExpression(text))
        {
            // Ifade item icindeki parent'li bir dugumu dondurebilir; cagiranlar baska yapilara
            // ekleyebildigi icin (raw.DeepClone() dali ile tutarli olacak sekilde) kopyalanir.
            return evaluator.EvaluateToNode(text, BuildContext(itemIndex))?.DeepClone();
        }

        return raw.DeepClone();
    }

    public JsonNode? ResolveValue(JsonNode? value, int itemIndex = 0)
    {
        switch (value)
        {
            case null:
                return null;
            case JsonObject obj:
                var result = new JsonObject();
                foreach (var pair in obj)
                {
                    result[pair.Key] = ResolveValue(pair.Value, itemIndex);
                }
                return result;
            case JsonArray array:
                var resolvedArray = new JsonArray();
                foreach (var element in array)
                {
                    resolvedArray.Add(ResolveValue(element, itemIndex));
                }
                return resolvedArray;
            case JsonValue jsonValue when jsonValue.TryGetValue<string>(out var text) && evaluator.ContainsExpression(text):
                // Tek-ifade cozumu item'in icindeki (parent'li) bir dugumu dondurebilir; yeni
                // yapiya eklemeden once kopyala, aksi halde "node already has a parent" hatasi olur.
                return evaluator.EvaluateToNode(text, BuildContext(itemIndex))?.DeepClone();
            default:
                return value.DeepClone();
        }
    }

    public async Task<string?> GetCredentialAsync(string type, string name, string field)
    {
        var store = services.GetService(typeof(ICredentialStore)) as ICredentialStore;
        if (store is null)
        {
            logSink("Credential store kayitli degil.");
            return null;
        }

        // Yeni referanslar credential Id'sini (Guid) tasir; eski kayitlar isim tasiyabilir.
        // Her iki yolda da sahiplik (actorOwnerId) dogrulanir: yalniz ayni sahibe ait credential cozulur.
        var data = Guid.TryParse(name, out var credentialId)
            ? await store.ResolveAsync(credentialId, actorOwnerId, cancellationToken)
            : await store.ResolveAsync(type, name, actorOwnerId, cancellationToken);

        if (data is null)
        {
            logSink($"Credential bulunamadi veya erisim yok: {type}/{name}");
            return null;
        }

        return data.TryGetValue(field, out var value) ? value : null;
    }

    public void Log(string message) => logSink(message);

    private ExpressionContext BuildContext(int itemIndex) => new()
    {
        CurrentItem = items.Count > 0 ? items[Math.Min(itemIndex, items.Count - 1)] : null,
        ItemIndex = itemIndex,
        RunIndex = runIndex,
        NodeOutputs = nodeOutputs,
        Trigger = trigger
    };
}
