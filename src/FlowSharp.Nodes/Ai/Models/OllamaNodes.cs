using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Ai.Models;

public sealed class OllamaChatNode : AiChatNodeBase
{
    protected override string Provider => "ollama";
    protected override string CredentialType => "ollamaApi";
    protected override string DefaultModel => "llama3.1";

    public override NodeDefinition Definition { get; } = new(
        Key: "ollama.chat",
        DisplayName: "Ollama",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Ai,
        Description: "Yerel Ollama sohbet tamamlamasi.",
        Parameters:
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true, HelpText: "ollamaApi tipli credential seçin."),
            PromptParam,
            ModelParam("llama3.1")
        ],
        Tags: ["ollama", "local", "ai"],
        Icon: "bot",
        IsAiPowered: true,
        Color: "#6b7280",
        Credentials: ["ollamaApi"]
    );
}

public sealed class OllamaChatModelNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "ollama.chatmodel",
        DisplayName: "Ollama Chat Model",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Ai,
        Description: "AI Agent icin yerel Ollama dil modeli saglar.",
        Parameters:
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true, HelpText: "ollamaApi tipli credential seçin."),
            new NodeParameterDefinition("model", "Model", NodeParameterType.String, DefaultValue: "llama3.1")
        ],
        Tags: ["ollama", "local", "ai"],
        Icon: "bot",
        Color: "#6b7280",
        IsAiPowered: true,
        Credentials: ["ollamaApi"],
        Inputs: [],
        Outputs: [new NodePort("model", "Model", NodePortType.AiModel)]
    );

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context) =>
        Task.FromResult(NodeExecutionResult.Single(context.Items));
}
