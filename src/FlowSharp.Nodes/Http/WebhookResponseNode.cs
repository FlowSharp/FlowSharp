using System.Text.Json.Nodes;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Http;

/// <summary>
/// Webhook ile baslayan bir workflow'da, cagirana donulecek HTTP yanitini sekillendirir
/// ("Respond to Webhook"). Webhook endpoint'i bu node'un ciktisini okuyup yanit olarak doner:
/// statusCode, body ve headers. Birden cok varsa son calisani gecerlidir.
/// </summary>
public sealed class WebhookResponseNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "webhook.response",
        DisplayName: "Respond to Webhook",
        Category: NodeCategory.Http,
        Kind: NodeKind.Action,
        Description: "Webhook cagirana ozel HTTP yaniti doner.",
        Parameters:
        [
            new NodeParameterDefinition("statusCode", "Status Code", NodeParameterType.Number, DefaultValue: "200"),
            new NodeParameterDefinition("body", "Body", NodeParameterType.Text,
                HelpText: "Duz metin veya JSON. Ornek: {{$json}} ya da {\"ok\":true}"),
            new NodeParameterDefinition("headers", "Headers (JSON)", NodeParameterType.Json, DefaultValue: "{}")
        ],
        Tags: ["http"],
        Icon: "send",
        Color: "#2f80ed");

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var statusCode = context.GetInt("statusCode", defaultValue: 200);
        var body = context.GetString("body") ?? "";
        var headers = context.GetJson("headers") as JsonObject ?? new JsonObject();

        var output = new JsonObject
        {
            ["statusCode"] = statusCode,
            ["body"] = body,
            ["headers"] = headers.DeepClone()
        };

        return Task.FromResult(NodeExecutionResult.Single(NodeItem.From(output)));
    }
}
