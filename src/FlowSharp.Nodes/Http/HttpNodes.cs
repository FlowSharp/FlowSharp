using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Http;

/// <summary>HTTP node'lari icin ortak istek mantigini tasiyan taban sinif.</summary>
public abstract class HttpNodeBase : PerItemNodeType
{
    /// <summary>Sabit metot (alt sinif). null ise "method" parametresinden okunur.</summary>
    protected virtual HttpMethod? FixedMethod => null;

    protected static NodeParameterDefinition UrlParam =>
        new("url", "URL", NodeParameterType.Url, IsRequired: true, HelpText: "Ornek: https://api.example.com/{{$json.id}}");

    protected static NodeParameterDefinition HeadersParam =>
        new("headers", "Headers (JSON)", NodeParameterType.Json, DefaultValue: "{}");

    protected static NodeParameterDefinition BodyParam =>
        new("body", "Body (JSON)", NodeParameterType.Json, DefaultValue: "{}",
            ShowWhen: new ParameterCondition("method", ["POST", "PUT", "PATCH"]));

    protected override async Task<NodeItem?> ProcessItemAsync(INodeExecutionContext context, NodeItem item, int index)
    {
        var url = context.GetString("url", index);
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("HTTP node icin 'url' parametresi gerekli.");
        }

        var method = FixedMethod ?? new HttpMethod((context.GetString("method", index) ?? "GET").ToUpperInvariant());
        using var request = new HttpRequestMessage(method, url);

        if (context.GetJson("headers", index) is JsonObject headers)
        {
            foreach (var header in headers)
            {
                if (header.Value is not null)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToString());
                }
            }
        }

        if (method != HttpMethod.Get && method != HttpMethod.Delete &&
            context.GetJson("body", index) is JsonNode body &&
            body is JsonObject or JsonArray)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        var factory = (IHttpClientFactory)context.Services.GetService(typeof(IHttpClientFactory))!;
        using var response = await factory.CreateClient("workflow-nodes").SendAsync(request, context.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(context.CancellationToken);

        var output = new JsonObject
        {
            ["statusCode"] = (int)response.StatusCode,
            ["success"] = response.IsSuccessStatusCode,
            ["body"] = Helpers.HttpHelper.TryParseJson(content),
            ["headers"] = ToHeaderObject(response)
        };

        if (!response.IsSuccessStatusCode)
        {
            context.Log($"HTTP {(int)response.StatusCode} dondu: {url}");
        }

        return NodeItem.From(output);
    }

    private static JsonObject ToHeaderObject(HttpResponseMessage response)
    {
        var headers = new JsonObject();
        foreach (var header in response.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        return headers;
    }
}

/// <summary>Tam donanimli HTTP istegi (metot secilebilir).</summary>
public sealed class HttpRequestNode : HttpNodeBase
{
    public override NodeDefinition Definition { get; } = new(
        Key: "http.request",
        DisplayName: "HTTP Request",
        Category: NodeCategory.Http,
        Kind: NodeKind.Action,
        Description: "Herhangi bir REST API'yi cagirir.",
        Parameters:
        [
            UrlParam,
            new NodeParameterDefinition("method", "Method", NodeParameterType.Select, IsRequired: true,
                DefaultValue: "GET", Options: ["GET", "POST", "PUT", "PATCH", "DELETE"]),
            HeadersParam,
            BodyParam
        ],
        Tags: ["http"],
        Icon: "globe",
        Color: "#2f80ed");
}

public sealed class HttpGetNode : HttpNodeBase
{
    protected override HttpMethod? FixedMethod => HttpMethod.Get;

    public override NodeDefinition Definition { get; } = new(
        "http.get", "HTTP GET", NodeCategory.Http, NodeKind.Action, "Bir API'den veri okur.",
        [UrlParam, HeadersParam], ["http"], "download", Color: "#2f80ed");
}

public sealed class HttpPostNode : HttpNodeBase
{
    protected override HttpMethod? FixedMethod => HttpMethod.Post;

    public override NodeDefinition Definition { get; } = new(
        "http.post", "HTTP POST", NodeCategory.Http, NodeKind.Action, "Bir API'ye veri gonderir.",
        [UrlParam, HeadersParam, BodyParam], ["http"], "upload", Color: "#2f80ed");
}

public sealed class HttpPutNode : HttpNodeBase
{
    protected override HttpMethod? FixedMethod => HttpMethod.Put;

    public override NodeDefinition Definition { get; } = new(
        "http.put", "HTTP PUT", NodeCategory.Http, NodeKind.Action, "Bir kaydi tumuyle gunceller.",
        [UrlParam, HeadersParam, BodyParam], ["http"], "upload", Color: "#2f80ed");
}

public sealed class HttpPatchNode : HttpNodeBase
{
    protected override HttpMethod? FixedMethod => HttpMethod.Patch;

    public override NodeDefinition Definition { get; } = new(
        "http.patch", "HTTP PATCH", NodeCategory.Http, NodeKind.Action, "Bir kaydi kismen gunceller.",
        [UrlParam, HeadersParam, BodyParam], ["http"], "edit", Color: "#2f80ed");
}

public sealed class HttpDeleteNode : HttpNodeBase
{
    protected override HttpMethod? FixedMethod => HttpMethod.Delete;

    public override NodeDefinition Definition { get; } = new(
        "http.delete", "HTTP DELETE", NodeCategory.Http, NodeKind.Action, "Bir kaydi siler.",
        [UrlParam, HeadersParam], ["http"], "trash", Color: "#eb5757");
}
