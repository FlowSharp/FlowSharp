using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowSharp.Application.Abstractions;
using FlowSharp.Application.Workflows;

namespace FlowSharp.Web.Endpoints;

/// <summary>Gelen HTTP isteklerini webhook.trigger node'larina baglayan endpoint'ler.</summary>
public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.Map("/webhook/{**path}", HandleAsync);
        return app;
    }

    private static async Task HandleAsync(
        string path,
        HttpContext httpContext,
        IWebhookRegistrar registrar,
        IWorkflowRunner runner,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var request = httpContext.Request;
        var response = httpContext.Response;
        var logger = loggerFactory.CreateLogger("WebhookEndpoints");

        var match = await registrar.ResolveAsync(request.Method, path, cancellationToken);
        if (match is null)
        {
            await WriteJsonAsync(response, StatusCodes.Status404NotFound,
                new JsonObject { ["error"] = $"'{request.Method} /webhook/{path}' icin kayitli webhook yok." }, cancellationToken);
            return;
        }

        var payload = await BuildPayloadAsync(path, match.NodeName, request, cancellationToken);

        WorkflowRunResult result;
        try
        {
            // Webhook senkron calisir: cagirana Respond to Webhook node ciktisini doneriz.
            result = await runner.ExecuteNowAsync(match.WorkflowId, payload, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Webhook calismasi sirasinda hata olustu. Method: {Method}, Path: {Path}, WorkflowId: {WorkflowId}",
                request.Method, path, match.WorkflowId);
            await WriteJsonAsync(response, StatusCodes.Status500InternalServerError,
                new JsonObject { ["error"] = "Webhook calismasi basarisiz." }, cancellationToken);
            return;
        }

        // Calisma sirasinda bir Respond to Webhook node'u varsa onun yanitini kullan.
        var responseNode = result.Nodes
            .LastOrDefault(node => node.NodeType.Equals("webhook.response", StringComparison.OrdinalIgnoreCase)
                && node.Status == NodeRunStatus.Succeeded);

        if (responseNode is not null &&
            responseNode.Output is JsonArray { Count: > 0 } items &&
            items[0] is JsonObject responseItem)
        {
            await WriteCustomResponseAsync(response, responseItem, cancellationToken);
            return;
        }

        // Respond node yoksa: basariliysa workflow ciktisini, degilse hatayi don.
        if (result.Succeeded)
        {
            await WriteJsonAsync(response, StatusCodes.Status200OK,
                result.Output.DeepClone(), cancellationToken);
        }
        else
        {
            logger.LogWarning("Webhook calismasi basarisiz. Method: {Method}, Path: {Path}, WorkflowId: {WorkflowId}, Error: {Error}",
                request.Method, path, match.WorkflowId, result.Error);
            await WriteJsonAsync(response, StatusCodes.Status500InternalServerError,
                new JsonObject { ["error"] = "Webhook calismasi basarisiz." }, cancellationToken);
        }
    }

    private static async Task WriteCustomResponseAsync(HttpResponse response, JsonObject responseItem, CancellationToken cancellationToken)
    {
        var statusCode = responseItem["statusCode"] is JsonValue sc && sc.TryGetValue<int>(out var code) ? code : 200;
        var body = responseItem["body"]?.GetValue<string>() ?? responseItem["body"]?.ToJsonString() ?? "";

        string? contentType = null;
        if (responseItem["headers"] is JsonObject headers)
        {
            foreach (var header in headers)
            {
                var value = header.Value?.ToString() ?? "";
                if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    contentType = value;
                    continue;
                }

                response.Headers[header.Key] = value;
            }
        }

        // Content-Type belirtilmemisse: govde gecerli JSON ise application/json, degilse text/plain.
        contentType ??= IsJson(body) ? "application/json" : "text/plain";

        response.StatusCode = statusCode;
        response.ContentType = contentType;
        await response.WriteAsync(body, Encoding.UTF8, cancellationToken);
    }

    private static bool IsJson(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        try
        {
            JsonNode.Parse(content);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task WriteJsonAsync(HttpResponse response, int statusCode, JsonNode body, CancellationToken cancellationToken)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        await response.WriteAsync(body.ToJsonString(), Encoding.UTF8, cancellationToken);
    }

    private static async Task<JsonDocument> BuildPayloadAsync(string path, string nodeName, HttpRequest request, CancellationToken cancellationToken)
    {
        var query = new JsonObject();
        foreach (var pair in request.Query)
        {
            query[pair.Key] = pair.Value.ToString();
        }

        var headers = new JsonObject();
        foreach (var header in request.Headers)
        {
            headers[header.Key] = header.Value.ToString();
        }

        JsonNode? body = null;
        if (request.ContentLength is > 0)
        {
            using var reader = new StreamReader(request.Body);
            var raw = await reader.ReadToEndAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    body = JsonNode.Parse(raw);
                }
                catch (JsonException)
                {
                    body = JsonValue.Create(raw);
                }
            }
        }

        var root = new JsonObject
        {
            ["source"] = "webhook",
            ["node"] = nodeName,
            ["method"] = request.Method,
            ["path"] = path,
            ["query"] = query,
            ["headers"] = headers,
            ["body"] = body
        };

        return JsonDocument.Parse(root.ToJsonString());
    }
}
