using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;
using System.Text.Json.Nodes;

namespace FlowSharp.Nodes.Ai.Tools
{
    /// <summary>HTTP araci. Agent bir sorgu vererek cagirir; yapilandirilmis URL'e GET atar.</summary>
    public sealed class HttpToolNode : NodeType
    {
        public override NodeDefinition Definition { get; } = new(
            Key: "tool.httpRequest",
            DisplayName: "HTTP Request Tool",
            Category: NodeCategory.Ai,
            Kind: NodeKind.Ai,
            Description: "Verilen sorguyla bir HTTP GET istegi yapar ve sonucu dondurur. Girdi: arama sorgusu.",
            Parameters:
            [
                new NodeParameterDefinition("url", "URL", NodeParameterType.Url, IsRequired: true,
                HelpText: "Ornek: https://api.example.com/search?q={{$json.input}}")
            ],
            Tags: ["ai", "tool"],
            Icon: "globe",
            Color: "#7d3cff",
            Inputs: [],
            Outputs: [new NodePort("tool", "Tool", NodePortType.AiTool)]);

        public override async Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
        {
            var url = context.GetString("url");
            if (string.IsNullOrWhiteSpace(url))
            {
                return NodeExecutionResult.Single(NodeItem.From(new JsonObject { ["error"] = "url bos." }));
            }

            var factory = (IHttpClientFactory)context.Services.GetService(typeof(IHttpClientFactory))!;
            using var response = await factory.CreateClient("workflow-nodes").GetAsync(url, context.CancellationToken);
            var body = await response.Content.ReadAsStringAsync(context.CancellationToken);
            return NodeExecutionResult.Single(NodeItem.From(new JsonObject
            {
                ["statusCode"] = (int)response.StatusCode,
                ["body"] = body
            }));
        }
    }
}
