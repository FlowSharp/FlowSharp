using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.SemanticKernel;

namespace FlowSharp.Nodes.Ai.Models;

public static class ModelConnectorFactory
{
    public static IKernelBuilder AddChatCompletionForProvider(
        this IKernelBuilder builder,
        string provider,
        string modelId,
        string apiKey,
        string? endpoint = null)
    {
        var prov = provider.ToLowerInvariant();

        switch (prov)
        {
            case "openai":
                builder.AddOpenAIChatCompletion(modelId, apiKey);
                break;

            case "azureopenai":
                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    throw new InvalidOperationException("Azure OpenAI icin Endpoint adresi gereklidir.");
                }
                builder.AddAzureOpenAIChatCompletion(modelId, endpoint, apiKey);
                break;

            case "anthropic":
                // Claude icin OpenAI API isteklerini Anthropic Messages API formatina ceviren handler kullanilir.
                builder.AddOpenAIChatCompletion(
                    modelId, 
                    apiKey, 
                    httpClient: new HttpClient(new AnthropicToOpenAiHandler(apiKey)));
                break;

            case "google":
            case "gemini":
                // Google Gemini OpenAI uyumlu endpoint
                builder.AddOpenAIChatCompletion(
                    modelId,
                    apiKey,
                    httpClient: new HttpClient(new OpenAiCompatibleHttpClientHandler("https://generativelanguage.googleapis.com/v1beta/openai/")));
                break;

            case "groq":
                builder.AddOpenAIChatCompletion(
                    modelId,
                    apiKey,
                    httpClient: new HttpClient(new OpenAiCompatibleHttpClientHandler("https://api.groq.com/openai/v1/")));
                break;

            case "ollama":
                builder.AddOpenAIChatCompletion(
                    modelId,
                    apiKey, // Ollama api key istemez ama bos birakilamaz
                    httpClient: new HttpClient(new OpenAiCompatibleHttpClientHandler(string.IsNullOrWhiteSpace(endpoint) ? "http://localhost:11434/v1/" : endpoint)));
                break;

            case "mistral":
                builder.AddOpenAIChatCompletion(
                    modelId,
                    apiKey,
                    httpClient: new HttpClient(new OpenAiCompatibleHttpClientHandler("https://api.mistral.ai/v1/")));
                break;

            case "cohere":
                builder.AddOpenAIChatCompletion(
                    modelId,
                    apiKey,
                    httpClient: new HttpClient(new OpenAiCompatibleHttpClientHandler("https://api.cohere.com/v1/")));
                break;

            case "huggingface":
                builder.AddOpenAIChatCompletion(
                    modelId,
                    apiKey,
                    httpClient: new HttpClient(new OpenAiCompatibleHttpClientHandler("https://api-inference.huggingface.co/v1/")));
                break;

            case "openrouter":
                builder.AddOpenAIChatCompletion(
                    modelId,
                    apiKey,
                    httpClient: new HttpClient(new OpenAiCompatibleHttpClientHandler("https://openrouter.ai/api/v1/")));
                break;

            default:
                throw new NotSupportedException($"AI saglayicisi '{provider}' desteklenmiyor.");
        }

        return builder;
    }
}
