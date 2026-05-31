using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;
using FlowSharp.Nodes.Helpers;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace FlowSharp.Nodes.Communication
{
    /// <summary>Slack'e gercek mesaj gonderir (chat.postMessage). Credential "slackApi": token (Bot Token).</summary>
    public sealed class SlackNode : PerItemNodeType
    {
        public override NodeDefinition Definition { get; } = new(
            "slack.message", "Slack", NodeCategory.Communication, NodeKind.Action, "Slack kanalina mesaj gonderir.",
            [
                new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true,
                HelpText: "slackApi tipli credential (token)"),
            new NodeParameterDefinition("channel", "Channel", NodeParameterType.String, IsRequired: true,
                HelpText: "Ornek: #genel veya C012AB3CD"),
            new NodeParameterDefinition("text", "Text", NodeParameterType.Text, IsRequired: true)
            ],
            ["communication"], "message-circle", Color: "#4a154b", Credentials: ["slackApi"]);

        protected override async Task<NodeItem?> ProcessItemAsync(INodeExecutionContext context, NodeItem item, int index)
        {
            var credName = context.GetString("_credential", index)!;
            var token = await context.GetCredentialAsync("slackApi", credName, "token")
                ?? throw new InvalidOperationException("slackApi credential 'token' eksik.");

            var payload = new JsonObject
            {
                ["channel"] = context.GetString("channel", index),
                ["text"] = context.GetString("text", index)
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://slack.com/api/chat.postMessage")
            {
                Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await HttpHelper.Client(context).SendAsync(request, context.CancellationToken);
            var body = await response.Content.ReadAsStringAsync(context.CancellationToken);
            return NodeItem.From(new JsonObject { ["statusCode"] = (int)response.StatusCode, ["response"] = HttpHelper.TryParseJson(body) });
        }
    }
}
