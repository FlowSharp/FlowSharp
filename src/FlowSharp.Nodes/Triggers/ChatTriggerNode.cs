using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;


namespace FlowSharp.Nodes.Triggers
{

    /// <summary>
    /// Sohbet tetikleyicisi. Designer'daki sohbet penceresinden gelen mesajla workflow'u baslatir;
    /// gelen mesaj <c>$json.chatInput</c> / <c>$json.text</c> olarak akar. ("When chat message received")
    /// </summary>
    public sealed class ChatTriggerNode : NodeType
    {
        public override NodeDefinition Definition { get; } = new(
            Key: "chat.trigger",
            DisplayName: "AI Chat UI",
            Category: NodeCategory.Trigger,
            Kind: NodeKind.Trigger,
            Description: "Sohbet penceresinden mesaj geldiginde workflow'u baslatir.",
            Parameters: [],
            Tags: ["trigger", "ai"],
            Icon: "message-circle",
            Color: "#10a37f",
            IsAiPowered: true);

        public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
        {
            var items = context.Items.Count > 0 ? context.Items : [NodeItem.Empty()];
            return Task.FromResult(NodeExecutionResult.Single(items));
        }
    }
}
