using System.Text.Json.Nodes;
using System.Text;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using FlowSharp.Application.Ai;
using FlowSharp.Application.Abstractions;
using FlowSharp.Application.Nodes;
using FlowSharp.Application.Nodes.Agents;
using FlowSharp.Application.Workflows;
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
        AgentSubNode? selectedModel = null;
        var modelStartedAt = DateTimeOffset.UtcNow;

        try
        {
            var model = request.Subs.FirstOrDefault(sub => sub.PortType == Domain.Nodes.NodePortType.AiModel);
            if (model is null)
            {
                return AgentResult.Fail("AI Agent bir Model alt-node'una baglanmali.");
            }

            selectedModel = model;
            modelStartedAt = DateTimeOffset.UtcNow;
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
                    (string query) => InvokeToolAsync(request, captured, query, request.ContextFactory),
                    functionName: Sanitize(tool.Name),
                    description: toolType.Definition.Description));
            }

            if (toolFunctions.Count > 0)
            {
                kernel.Plugins.AddFromFunctions("tools", toolFunctions);
            }

            var agentCtx = request.ContextFactory(request.AgentType, request.AgentName, request.AgentParameters, request.Input);
            var systemPrompt = agentCtx.GetString("systemPrompt") ?? "";
            var userText = ResolveAgentUserText(agentCtx.GetString("text"), request.Input);
            var memoryContext = await BuildMemoryContextAsync(request, userText);

            var settings = new OpenAIPromptExecutionSettings
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                Temperature = 0
            };

            var prompt = string.IsNullOrWhiteSpace(memoryContext)
                ? $"{systemPrompt}\n\nKullanici: {userText}"
                : $"{systemPrompt}\n\nBaglam:\n{memoryContext}\n\nKullanici: {userText}";

            var output = request.OnTextDelta is null
                ? (await kernel.InvokePromptAsync(
                    prompt,
                    new KernelArguments(settings),
                    cancellationToken: cancellationToken)).ToString()
                : await InvokePromptStreamingAsync(kernel, prompt, settings, request.OnTextDelta, cancellationToken);

            await NotifySubNodeAsync(request, model, NodeRunStatus.Succeeded, new JsonObject
            {
                ["provider"] = provider,
                ["model"] = modelId
            }, null, modelStartedAt, 1);

            await SaveConversationMemoryAsync(request, userText, output, cancellationToken);

            return AgentResult.Ok(NodeItem.From(new JsonObject { ["output"] = output }));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpOperationException exception)
        {
            logger.LogError(exception, "AI Agent '{Name}' model servisi hatasi.", request.AgentName);
            if (selectedModel is not null)
            {
                await NotifySubNodeAsync(request, selectedModel, NodeRunStatus.Failed, new JsonObject(), FormatAiServiceError(exception), modelStartedAt, 0);
            }
            return AgentResult.Fail(FormatAiServiceError(exception));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "AI Agent '{Name}' hatasi.", request.AgentName);
            if (selectedModel is not null)
            {
                await NotifySubNodeAsync(request, selectedModel, NodeRunStatus.Failed, new JsonObject(), "Model cagrisi basarisiz oldu.", modelStartedAt, 0);
            }
            return AgentResult.Fail("AI Agent calismasi sirasinda beklenmeyen bir hata olustu. Detaylar loglara yazildi.");
        }
    }

    private static string FormatAiServiceError(HttpOperationException exception)
    {
        var statusCode = exception.StatusCode;
        return statusCode switch
        {
            HttpStatusCode.ServiceUnavailable =>
                "Model servisi su anda kullanilamiyor (503). Biraz sonra tekrar deneyin veya model/endpoint ayarlarini kontrol edin.",
            HttpStatusCode.TooManyRequests =>
                "Model servisi hiz limitine takildi (429). Biraz sonra tekrar deneyin veya kota/limit ayarlarini kontrol edin.",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                $"Model servisi kimlik dogrulamayi reddetti ({(int)statusCode.Value}). API anahtari ve credential ayarlarini kontrol edin.",
            HttpStatusCode.NotFound =>
                "Model servisi secilen modeli veya endpoint'i bulamadi (404). Model/deployment adini kontrol edin.",
            { } code =>
                $"Model servisi istegi basarisiz oldu ({(int)code}). Detaylar loglara yazildi.",
            null =>
                "Model servisi istegi basarisiz oldu. Detaylar loglara yazildi."
        };
    }

    private static async Task<string> InvokePromptStreamingAsync(
        Kernel kernel,
        string prompt,
        OpenAIPromptExecutionSettings settings,
        Func<string, Task> onTextDelta,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        await foreach (var chunk in kernel.InvokePromptStreamingAsync(
            prompt,
            new KernelArguments(settings),
            cancellationToken: cancellationToken))
        {
            var text = chunk.ToString();
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            output.Append(text);
            await onTextDelta(text);
        }

        return output.ToString();
    }

    private async Task<string> BuildMemoryContextAsync(AgentRequest request, string userText)
    {
        var memories = new List<string>();

        foreach (var memory in request.Subs.Where(sub => sub.PortType == Domain.Nodes.NodePortType.AiMemory))
        {
            var startedAt = DateTimeOffset.UtcNow;
            var item = NodeItem.From(new JsonObject
            {
                ["input"] = userText,
                ["text"] = userText
            });
            var parameters = NormalizeMemoryParameters(memory.Parameters, userText);
            var context = request.ContextFactory(memory.Type, memory.Name, parameters, [item]);
            var memoryType = registry.Find(memory.Type);

            if (memoryType is null)
            {
                await NotifySubNodeAsync(request, memory, NodeRunStatus.Skipped, new JsonObject(), "Memory node tipi bulunamadi.", startedAt, 0);
                continue;
            }

            NodeExecutionResult result;
            try
            {
                result = await memoryType.ExecuteAsync(context);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "AI Agent '{Name}' memory node '{Memory}' calistirilamadi.", request.AgentName, memory.Name);
                await NotifySubNodeAsync(request, memory, NodeRunStatus.Failed, new JsonObject(), "Memory sorgusu calistirilamadi. Detaylar loglara yazildi.", startedAt, 0);
                continue;
            }

            if (!result.Succeeded)
            {
                await NotifySubNodeAsync(request, memory, NodeRunStatus.Failed, new JsonObject(), result.Error, startedAt, 0);
                continue;
            }

            var output = new JsonArray();
            foreach (var match in result.PrimaryItems)
            {
                output.Add(match.Json.DeepClone());
                var text = match.Json["text"]?.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    memories.Add($"- {text}");
                }
            }

            await NotifySubNodeAsync(request, memory, NodeRunStatus.Succeeded, output, null, startedAt, result.PrimaryItems.Count);
        }

        return string.Join('\n', memories);
    }

    private async Task SaveConversationMemoryAsync(
        AgentRequest request,
        string userText,
        string assistantText,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userText) || string.IsNullOrWhiteSpace(assistantText))
        {
            return;
        }

        foreach (var memory in request.Subs.Where(sub => sub.PortType == Domain.Nodes.NodePortType.AiMemory))
        {
            var item = NodeItem.From(new JsonObject
            {
                ["input"] = userText,
                ["text"] = userText
            });
            var parameters = NormalizeMemoryParameters(memory.Parameters, userText);
            var context = request.ContextFactory(memory.Type, memory.Name, parameters, [item]);

            try
            {
                var embedder = context.Services.GetRequiredService<IEmbeddingGenerator>();
                var store = context.Services.GetRequiredService<IVectorStore>();
                var scope = context.WorkflowId?.ToString("N") ?? "global";
                var collection = context.GetString("collection") ?? "default";
                var text = $"Kullanici: {userText}\nAsistan: {assistantText}";
                var vector = (await embedder.EmbedAsync([text], cancellationToken))[0];
                var metadata = new JsonObject
                {
                    ["type"] = "chat",
                    ["user"] = userText,
                    ["assistant"] = assistantText,
                    ["createdAt"] = DateTimeOffset.UtcNow.ToString("O")
                }.ToJsonString();

                await store.UpsertAsync(scope, collection,
                    [new VectorRecord($"chat-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}", text, vector, metadata)],
                    cancellationToken);

                await NotifySubNodeAsync(request, memory, NodeRunStatus.Succeeded, new JsonObject
                {
                    ["collection"] = collection,
                    ["inserted"] = 1
                }, null, DateTimeOffset.UtcNow, 1);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "AI Agent '{Name}' konusma hafizasi kaydedilemedi.", request.AgentName);
                await NotifySubNodeAsync(request, memory, NodeRunStatus.Failed, new JsonObject(), "Konusma hafizasi kaydedilemedi. Detaylar loglara yazildi.", DateTimeOffset.UtcNow, 0);
            }
        }
    }

    private static JsonObject NormalizeMemoryParameters(JsonObject parameters, string userText)
    {
        var normalized = parameters.DeepClone().AsObject();
        var query = normalized.TryGetPropertyValue("query", out var value) ? value?.ToString() : null;
        if (string.IsNullOrWhiteSpace(query) ||
            query.Equals("{{$json.input}}", StringComparison.OrdinalIgnoreCase) ||
            query.Equals("{{$json.text}}", StringComparison.OrdinalIgnoreCase))
        {
            normalized["query"] = userText;
        }

        return normalized;
    }

    private static string ResolveAgentUserText(string? configuredText, IReadOnlyList<NodeItem> input)
    {
        if (!string.IsNullOrWhiteSpace(configuredText))
        {
            return configuredText;
        }

        var json = input.FirstOrDefault()?.Json;
        if (json is null)
        {
            return string.Empty;
        }

        foreach (var key in new[] { "text", "chatInput", "input", "message", "value" })
        {
            if (json.TryGetPropertyValue(key, out var value) && value is not null)
            {
                var text = value.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return json.ToJsonString();
    }

    private async Task<string> InvokeToolAsync(AgentRequest request, AgentSubNode tool, string query, AgentContextFactory contextFactory)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var toolType = registry.Find(tool.Type);
        if (toolType is null)
        {
            await NotifySubNodeAsync(request, tool, NodeRunStatus.Skipped, new JsonObject(), "Arac bulunamadi.", startedAt, 0);
            return "Arac bulunamadi.";
        }

        var item = NodeItem.From(new JsonObject { ["input"] = query });
        var context = contextFactory(tool.Type, tool.Name, tool.Parameters, [item]);
        var result = await toolType.ExecuteAsync(context);
        var primary = result.PrimaryItems.FirstOrDefault();
        await NotifySubNodeAsync(request, tool, result.Succeeded ? NodeRunStatus.Succeeded : NodeRunStatus.Failed,
            primary?.Json.DeepClone() ?? new JsonObject(), result.Error, startedAt, result.PrimaryItems.Count);
        return primary?.Json.ToJsonString() ?? "";
    }

    private static async Task NotifySubNodeAsync(
        AgentRequest? request,
        AgentSubNode subNode,
        NodeRunStatus status,
        JsonNode output,
        string? error,
        DateTimeOffset startedAt,
        int itemCount)
    {
        if (request?.OnSubNodeCompleted is null)
        {
            return;
        }

        await request.OnSubNodeCompleted(new NodeRunData(
            subNode.Id,
            subNode.Name,
            subNode.Type,
            status,
            output,
            error,
            startedAt,
            DateTimeOffset.UtcNow,
            itemCount));
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
