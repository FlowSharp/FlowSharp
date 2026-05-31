using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Triggers
{
    /// <summary>
    /// HTTP webhook tetikleyicisi. "path" ve "method" parametreleriyle bir URL'e baglanir;
    /// gelen istek payload'i workflow'un giris item'i olur.
    /// </summary>
    public sealed class WebhookTriggerNode : NodeType
    {
        public override NodeDefinition Definition { get; } = new(
            Key: "webhook.trigger",
            DisplayName: "Webhook",
            Category: NodeCategory.Trigger,
            Kind: NodeKind.Trigger,
            Description: "Bir HTTP istegi geldiginde workflow'u baslatir.",
            Parameters:
            [
                new NodeParameterDefinition("path", "Path", NodeParameterType.String, IsRequired: true,
                DefaultValue: "my-webhook", HelpText: "URL: /webhook/{path}"),
            new NodeParameterDefinition("method", "Method", NodeParameterType.Select, IsRequired: true,
                DefaultValue: "POST", Options: ["GET", "POST", "PUT", "PATCH", "DELETE"])
            ],
            Tags: ["trigger"],
            Icon: "globe",
            Color: "#7d7d87");

        public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
        {
            var items = context.Items.Count > 0 ? context.Items : [NodeItem.Empty()];
            return Task.FromResult(NodeExecutionResult.Single(items));
        }
    }
}
