using System.Text.Json.Nodes;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using FlowSharp.Application.Credentials;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Credentials;
using FlowSharp.Domain.Nodes;
using FlowSharp.Nodes.Credentials;

namespace FlowSharp.Nodes.Ai.Models;

/// <summary>
/// Tüm AI sağlayıcıları için ortak sohbet tamamlaması yapan taban sınıf.
/// </summary>
public abstract class AiChatNodeBase : PerItemNodeType, IProvidesCredentials
{
    protected abstract string Provider { get; }
    protected abstract string CredentialType { get; }
    protected abstract string DefaultModel { get; }

    // Varsayilan: sadece API Key. Azure/Ollama gibi farkli alan isteyenler override eder.
    public virtual IEnumerable<CredentialSchema> CredentialSchemas =>
        [new CredentialSchema(CredentialType, Provider, CredentialFields.ApiKey())];

    // Opsiyonel sistem talimati (modelin rolu). Sohbet API'sinde "system" rolu olarak gonderilir.
    protected static NodeParameterDefinition SystemPromptParam =>
        new("systemPrompt", "System Prompt", NodeParameterType.Text, IsRequired: false,
            HelpText: "Opsiyonel: modelin rolu/talimati. Ornek: Sen yardimci bir asistansin.");

    // Kullanici girdisi (sohbet API'sinde "user" rolu). Eski "Prompt" alaninin yerini alir.
    protected static NodeParameterDefinition PromptParam =>
        new("prompt", "User Input", NodeParameterType.Text, IsRequired: true,
            HelpText: "Kullanici mesaji. Ornek: {{$json.text}}");

    protected static NodeParameterDefinition ModelParam(string defaultModel) =>
        new("model", "Model", NodeParameterType.String, DefaultValue: defaultModel);

    protected override async Task<NodeItem?> ProcessItemAsync(INodeExecutionContext context, NodeItem item, int index)
    {
        var prompt = context.GetString("prompt", index);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new InvalidOperationException($"{Provider} AI node icin 'prompt' (User Input) parametresi gerekli.");
        }

        var systemPrompt = context.GetString("systemPrompt", index);

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

        // Sohbet API'sini system/user rolleriyle kullan (system opsiyonel).
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var history = new ChatHistory();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            history.AddSystemMessage(systemPrompt);
        }

        history.AddUserMessage(prompt);
        var result = await chat.GetChatMessageContentAsync(history, kernel: kernel, cancellationToken: context.CancellationToken);

        return NodeItem.From(new JsonObject
        {
            ["provider"] = Provider,
            ["model"] = modelId,
            ["text"] = result.Content
        });
    }
}
