using System.Text;
using System.Text.Json.Nodes;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;
using FlowSharp.Nodes.Helpers;

namespace FlowSharp.Nodes.Communication
{
    /// <summary>Discord webhook'una gercek mesaj gonderir. Webhook URL parametre olarak verilir.</summary>
    public sealed class DiscordNode : PerItemNodeType
    {
        public override NodeDefinition Definition { get; } = new(
            "discord.message", "Discord", NodeCategory.Communication, NodeKind.Action, "Discord webhook'una mesaj gonderir.",
            [
                new NodeParameterDefinition("webhookUrl", "Webhook URL", NodeParameterType.Url, IsRequired: true),
            new NodeParameterDefinition("content", "Content", NodeParameterType.Text, IsRequired: true)
            ],
            ["communication"], "message-square", Color: "#5865f2");

        protected override async Task<NodeItem?> ProcessItemAsync(INodeExecutionContext context, NodeItem item, int index)
        {
            var url = context.GetString("webhookUrl", index)
                ?? throw new InvalidOperationException("Discord node icin webhookUrl gerekli.");
            var payload = new JsonObject { ["content"] = context.GetString("content", index) };

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
            };

            using var response = await HttpHelper.Client(context).SendAsync(request, context.CancellationToken);
            return NodeItem.From(new JsonObject { ["statusCode"] = (int)response.StatusCode, ["sent"] = response.IsSuccessStatusCode });
        }
    }
}
