using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Ai.Models;

public sealed class OpenRouterChatNode : AiChatNodeBase
{
    protected override string Provider => "openrouter";
    protected override string CredentialType => "openRouterApi";
    protected override string DefaultModel => "google/gemini-2.5-flash";

    public override NodeDefinition Definition { get; } = new(
        Key: "openrouter.chat",
        DisplayName: "OpenRouter",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Ai,
        Description: "OpenRouter üzerinden yüzlerce dil modeline erişim sağlar.",
        Parameters:
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true, HelpText: "openRouterApi tipli credential seçin."),
            SystemPromptParam,
            PromptParam,
            ModelParam("google/gemini-2.5-flash")
        ],
        Tags: ["openrouter", "ai"],
        Icon: "robot",
        IsAiPowered: true,
        Color: "#7c3aed",
        Credentials: ["openRouterApi"],
        SubCategory: "AI Chat Nodes"
    );
}

public sealed class OpenRouterChatModelNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "openrouter.chatmodel",
        DisplayName: "OpenRouter Chat Model",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Ai,
        Description: "AI Agent icin OpenRouter dil modeli saglar.",
        Parameters:
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true, HelpText: "openRouterApi tipli credential seçin."),
            new NodeParameterDefinition("model", "Model", NodeParameterType.String, DefaultValue: "google/gemini-2.5-flash")
        ],
        Tags: ["openrouter", "ai"],
        Icon: "robot",
        Color: "#7c3aed",
        IsAiPowered: true,
        Credentials: ["openRouterApi"],
        SubCategory: "AI Agents Model",
        Inputs: [],
        Outputs: [new NodePort("model", "Model", NodePortType.AiModel)]
    );

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context) =>
        Task.FromResult(NodeExecutionResult.Single(context.Items));
}
