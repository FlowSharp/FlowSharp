using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using FlowSharp.Application.Abstractions;
using FlowSharp.Application.Nodes;
using FlowSharp.Application.Nodes.Agents;
using FlowSharp.Nodes.Ai.Models; // AddChatCompletionForProvider

namespace FlowSharp.Nodes.Ai.Agent;

/// <summary>
/// AI Agent orkestrasyonunun Semantic Kernel implementasyonu. Provider eslemesi, credential
/// cozumu, arac (tool) baglama ve LLM cagrisi burada (Nodes/AI katmani) yasar; generic workflow
/// motoru yalniz <see cref="IAgentExecutor"/> uzerinden cagirir.
/// </summary>
public sealed class SemanticKernelAgentExecutor(
    INodeRegistry registry,
    ICredentialStore credentialStore,
    ILogger<SemanticKernelAgentExecutor> logger) : IAgentExecutor
{
    public async Task<AgentResult> ExecuteAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var model = request.Subs.FirstOrDefault(sub => sub.PortType == Domain.Nodes.NodePortType.AiModel);
            if (model is null)
            {
                return AgentResult.Fail("AI Agent bir Model alt-node'una baglanmali.");
            }

            var (provider, credentialType, defaultModel) = GetProviderDetails(model.Type);
            var (apiKey, endpoint, deploymentName) = await ResolveModelCredentialsAsync(model, credentialType, cancellationToken);

            if (string.IsNullOrWhiteSpace(apiKey) && !provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
            {
                return AgentResult.Fail($"{model.Name} ({provider}) icin API anahtari bulunamadi. Lutfen credential baglayin.");
            }

            var modelId = model.Parameters.TryGetPropertyValue("model", out var m) ? m?.ToString() : null;
            if (string.IsNullOrWhiteSpace(modelId))
            {
                modelId = !string.IsNullOrWhiteSpace(deploymentName) ? deploymentName : defaultModel;
            }

            var builder = Kernel.CreateBuilder();
            builder.AddChatCompletionForProvider(provider, modelId, apiKey ?? "", endpoint);
            var kernel = builder.Build();

            var toolFunctions = new List<KernelFunction>();
            foreach (var tool in request.Subs.Where(sub => sub.PortType == Domain.Nodes.NodePortType.AiTool))
            {
                var toolType = registry.Find(tool.Type);
                if (toolType is null)
                {
                    continue;
                }

                var captured = tool;
                toolFunctions.Add(KernelFunctionFactory.CreateFromMethod(
                    (string query) => InvokeToolAsync(captured, query, request.ContextFactory),
                    functionName: Sanitize(tool.Name),
                    description: toolType.Definition.Description));
            }

            if (toolFunctions.Count > 0)
            {
                kernel.Plugins.AddFromFunctions("tools", toolFunctions);
            }

            var agentCtx = request.ContextFactory(request.AgentType, request.AgentName, request.AgentParameters, request.Input);
            var systemPrompt = agentCtx.GetString("systemPrompt") ?? "";
            var userText = agentCtx.GetString("text") ?? "";

            var settings = new OpenAIPromptExecutionSettings
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                Temperature = 0
            };

            var result = await kernel.InvokePromptAsync(
                $"{systemPrompt}\n\nKullanici: {userText}",
                new KernelArguments(settings),
                cancellationToken: cancellationToken);

            return AgentResult.Ok(NodeItem.From(new JsonObject { ["output"] = result.ToString() }));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "AI Agent '{Name}' hatasi.", request.AgentName);
            return AgentResult.Fail(exception.Message);
        }
    }

    private async Task<string> InvokeToolAsync(AgentSubNode tool, string query, AgentContextFactory contextFactory)
    {
        var toolType = registry.Find(tool.Type);
        if (toolType is null)
        {
            return "Arac bulunamadi.";
        }

        var item = NodeItem.From(new JsonObject { ["input"] = query });
        var context = contextFactory(tool.Type, tool.Name, tool.Parameters, [item]);
        var result = await toolType.ExecuteAsync(context);
        return result.PrimaryItems.FirstOrDefault()?.Json.ToJsonString() ?? "";
    }

    private static (string Provider, string CredentialType, string DefaultModel) GetProviderDetails(string modelType) =>
        modelType.ToLowerInvariant() switch
        {
            "azureopenai.chatmodel" => ("azureopenai", "azureOpenAiApi", "gpt-4o-mini"),
            "gemini.chatmodel" => ("gemini", "googleGeminiApi", "gemini-2.5-flash"),
            "anthropic.chatmodel" => ("anthropic", "anthropicApi", "claude-3-5-sonnet-20241022"),
            "groq.chatmodel" => ("groq", "groqApi", "llama-3.3-70b-versatile"),
            "ollama.chatmodel" => ("ollama", "ollamaApi", "llama3.1"),
            "mistral.chatmodel" => ("mistral", "mistralApi", "mistral-large-latest"),
            "cohere.chatmodel" => ("cohere", "cohereApi", "command-r-plus"),
            "huggingface.chatmodel" => ("huggingface", "huggingFaceApi", "meta-llama/Llama-3.3-70B-Instruct"),
            "openrouter.chatmodel" => ("openrouter", "openRouterApi", "google/gemini-2.5-flash"),
            _ => ("openai", "openAiApi", "gpt-4o-mini")
        };

    private async Task<(string? ApiKey, string? Endpoint, string? DeploymentName)> ResolveModelCredentialsAsync(
        AgentSubNode model, string credentialType, CancellationToken cancellationToken)
    {
        var credName = model.Parameters.TryGetPropertyValue("_credential", out var c) ? c?.ToString() : null;
        if (!string.IsNullOrWhiteSpace(credName))
        {
            var data = await credentialStore.ResolveAsync(credentialType, credName, cancellationToken);
            if (data is not null)
            {
                data.TryGetValue("apiKey", out var apiKey);
                data.TryGetValue("endpoint", out var endpoint);
                data.TryGetValue("deploymentName", out var deploymentName);
                return (apiKey, endpoint, deploymentName);
            }
        }

        return (null, null, null);
    }

    private static string Sanitize(string name)
    {
        var cleaned = new string(name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
        return string.IsNullOrEmpty(cleaned) || !char.IsLetter(cleaned[0]) ? "tool_" + cleaned : cleaned;
    }
}
