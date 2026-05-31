using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;
using FlowSharp.Nodes.Helpers;
using System.Text;
using System.Text.Json.Nodes;

namespace FlowSharp.Nodes.Communication
{
    /// <summary>Telegram'a gercek mesaj gonderir. Credential "telegramApi": token (Bot Token).</summary>
    public sealed class TelegramNode : PerItemNodeType
    {
        public override NodeDefinition Definition { get; } = new(
            "telegram.message", "Telegram", NodeCategory.Communication, NodeKind.Action, "Telegram sohbetine mesaj gonderir.",
            [
                new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true,
                HelpText: "telegramApi tipli credential (token)"),
            new NodeParameterDefinition("chatId", "Chat ID", NodeParameterType.String, IsRequired: true),
            new NodeParameterDefinition("text", "Text", NodeParameterType.Text, IsRequired: true)
            ],
            ["communication"], "send", Color: "#229ed9", Credentials: ["telegramApi"]);

        protected override async Task<NodeItem?> ProcessItemAsync(INodeExecutionContext context, NodeItem item, int index)
        {
            var credName = context.GetString("_credential", index)!;
            var token = await context.GetCredentialAsync("telegramApi", credName, "token")
                ?? throw new InvalidOperationException("telegramApi credential 'token' eksik.");

            var payload = new JsonObject
            {
                ["chat_id"] = context.GetString("chatId", index),
                ["text"] = context.GetString("text", index)
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.telegram.org/bot{token}/sendMessage")
            {
                Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
            };

            using var response = await HttpHelper.Client(context).SendAsync(request, context.CancellationToken);
            var body = await response.Content.ReadAsStringAsync(context.CancellationToken);
            return NodeItem.From(new JsonObject { ["statusCode"] = (int)response.StatusCode, ["response"] = HttpHelper.TryParseJson(body) });
        }
    }
}
