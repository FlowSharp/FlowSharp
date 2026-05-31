using System.Text.Json.Nodes;
using Microsoft.SemanticKernel;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Ai.Models;

/// <summary>
/// Tüm AI sağlayıcıları için ortak sohbet tamamlaması yapan taban sınıf.
/// </summary>
public abstract class AiChatNodeBase : PerItemNodeType
{
    protected abstract string Provider { get; }
    protected abstract string CredentialType { get; }
    protected abstract string DefaultModel { get; }

    protected static NodeParameterDefinition PromptParam =>
        new("prompt", "Prompt", NodeParameterType.Text, IsRequired: true,
            HelpText: "Ornek: Su metni ozetle: {{$json.text}}");

    protected static NodeParameterDefinition ModelParam(string defaultModel) =>
        new("model", "Model", NodeParameterType.String, DefaultValue: defaultModel);

    protected override async Task<NodeItem?> ProcessItemAsync(INodeExecutionContext context, NodeItem item, int index)
    {
        var prompt = context.GetString("prompt", index);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new InvalidOperationException($"{Provider} AI node icin 'prompt' parametresi gerekli.");
        }

        var credName = context.GetString("_credential", index);
        if (string.IsNullOrWhiteSpace(credName))
        {
            throw new InvalidOperationException($"{Provider} AI node icin kimlik bilgisi (Credential) secilmelidir.");
        }

        // Credential bilgilerini coz
        var apiKey = await context.GetCredentialAsync(CredentialType, credName, "apiKey") ?? "";
        var endpoint = await context.GetCredentialAsync(CredentialType, credName, "endpoint");
        
        // Azure OpenAI'da deploymentName modelId olarak kullanilir, yoksa model parametresinden alinir.
        var deploymentName = await context.GetCredentialAsync(CredentialType, credName, "deploymentName");

        var modelId = context.GetString("model", index);
        if (string.IsNullOrWhiteSpace(modelId))
        {
            modelId = !string.IsNullOrWhiteSpace(deploymentName) ? deploymentName : DefaultModel;
        }

        // Ollama api key gerektirmez
        if (string.IsNullOrWhiteSpace(apiKey) && !Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Secilen '{credName}' kimlik bilgisinde 'apiKey' degeri bulunamadi.");
        }

        var builder = Kernel.CreateBuilder();
        builder.AddChatCompletionForProvider(Provider, modelId, apiKey, endpoint);

        var kernel = builder.Build();
        var result = await kernel.InvokePromptAsync(prompt, cancellationToken: context.CancellationToken);

        return NodeItem.From(new JsonObject
        {
            ["provider"] = Provider,
            ["model"] = modelId,
            ["text"] = result.ToString()
        });
    }
}
