using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowSharp.Application.Json;

namespace FlowSharp.Nodes.Ai.Models;

/// <summary>
/// OpenAI /v1/chat/completions isteklerini Anthropic /v1/messages formatina ceviren ve donusturen handler.
/// </summary>
public sealed class AnthropicToOpenAiHandler : DelegatingHandler
{
    private readonly string _apiKey;

    public AnthropicToOpenAiHandler(string apiKey)
    {
        _apiKey = apiKey;
        InnerHandler = new HttpClientHandler();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content == null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var requestBodyStr = await request.Content.ReadAsStringAsync(cancellationToken);
        var openAiRequest = JsonSerializer.Deserialize<JsonObject>(requestBodyStr);
        if (openAiRequest == null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var model = openAiRequest["model"]?.ToString() ?? "claude-3-5-sonnet-20241022";
        var messagesArray = openAiRequest["messages"] as JsonArray;
        var maxTokens = openAiRequest["max_tokens"]?.GetValue<int>() ?? 2048;
        var temperature = openAiRequest["temperature"]?.GetValue<double>() ?? 0.0;

        string? systemPrompt = null;
        var anthropicMessages = new JsonArray();

        if (messagesArray != null)
        {
            foreach (var node in messagesArray)
            {
                if (node is JsonObject msg)
                {
                    var role = msg["role"]?.ToString();
                    var content = msg["content"]?.ToString() ?? "";

                    if (role == "system")
                    {
                        systemPrompt = content;
                    }
                    else
                    {
                        anthropicMessages.Add(new JsonObject
                        {
                            ["role"] = role == "assistant" ? "assistant" : "user",
                            ["content"] = content
                        });
                    }
                }
            }
        }

        var anthropicRequest = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = maxTokens,
            ["messages"] = anthropicMessages,
            ["temperature"] = temperature
        };

        if (systemPrompt != null)
        {
            anthropicRequest["system"] = systemPrompt;
        }

        using var anthropicHttpReq = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        anthropicHttpReq.Headers.Add("x-api-key", _apiKey);
        anthropicHttpReq.Headers.Add("anthropic-version", "2023-06-01");
        anthropicHttpReq.Content = new StringContent(anthropicRequest.ToJsonString(FlowJson.Relaxed), System.Text.Encoding.UTF8, "application/json");

        using var client = new HttpClient();
        var anthropicHttpResp = await client.SendAsync(anthropicHttpReq, cancellationToken);
        var anthropicRespStr = await anthropicHttpResp.Content.ReadAsStringAsync(cancellationToken);

        if (!anthropicHttpResp.IsSuccessStatusCode)
        {
            var errResponse = new HttpResponseMessage(anthropicHttpResp.StatusCode)
            {
                Content = new StringContent(anthropicRespStr, System.Text.Encoding.UTF8, "application/json")
            };
            return errResponse;
        }

        var anthropicResponse = JsonSerializer.Deserialize<JsonObject>(anthropicRespStr);
        var responseContent = "";
        var finishReason = "stop";

        if (anthropicResponse != null)
        {
            var contentArr = anthropicResponse["content"] as JsonArray;
            if (contentArr != null && contentArr.Count > 0)
            {
                responseContent = contentArr[0]?["text"]?.ToString() ?? "";
            }
            var stopReason = anthropicResponse["stop_reason"]?.ToString();
            if (stopReason == "max_tokens")
            {
                finishReason = "length";
            }
        }

        var openAiResponse = new JsonObject
        {
            ["id"] = anthropicResponse?["id"]?.ToString() ?? ("chatcmpl-" + Guid.NewGuid().ToString("N")),
            ["object"] = "chat.completion",
            ["created"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["model"] = model,
            ["choices"] = new JsonArray
            {
                new JsonObject
                {
                    ["index"] = 0,
                    ["message"] = new JsonObject
                    {
                        ["role"] = "assistant",
                        ["content"] = responseContent
                    },
                    ["finish_reason"] = finishReason
                }
            }
        };

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(openAiResponse.ToJsonString(FlowJson.Relaxed), System.Text.Encoding.UTF8, "application/json")
        };
        return response;
    }
}
