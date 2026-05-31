using System.Text.Json;
using System.Text.Json.Nodes;
using FlowSharp.Application.Nodes;

namespace FlowSharp.Nodes.Helpers
{
    internal static class HttpHelper
    {
        public static HttpClient Client(INodeExecutionContext context) =>
            ((IHttpClientFactory)context.Services.GetService(typeof(IHttpClientFactory))!).CreateClient("workflow-nodes");

        /// <summary>Yaniti JSON olarak ayristirir; gecerli JSON degilse ham metni JsonValue olarak dondurur.</summary>
        public static JsonNode? TryParseJson(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            try
            {
                return JsonNode.Parse(content);
            }
            catch (JsonException)
            {
                return JsonValue.Create(content);
            }
        }
    }
}
