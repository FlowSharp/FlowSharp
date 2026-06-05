using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Ai.Models;

public sealed class AnthropicChatNode : AiChatNodeBase
{
    protected override string Provider => "anthropic";
    protected override string CredentialType => "anthropicApi";
    protected override string DefaultModel => "claude-3-5-sonnet-20241022";

    public override NodeDefinition Definition { get; } = new(
        Key: "anthropic.chat",
        DisplayName: "Anthropic Claude",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Ai,
        Description: "Anthropic Claude sohbet tamamlamasi.",
        Parameters:
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true, HelpText: "anthropicApi tipli credential seçin."),
            SystemPromptParam,
            PromptParam,
            ModelParam("claude-3-5-sonnet-20241022")
        ],
        Tags: ["anthropic", "claude", "ai"],
        Icon: "robot",
        IsAiPowered: true,
        Color: "#d97706",
        Credentials: ["anthropicApi"],
        SubCategory: "AI Chat Nodes"
    );
}

public sealed class AnthropicChatModelNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "anthropic.chatmodel",
        DisplayName: "Anthropic Chat Model",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Ai,
        Description: "AI Agent icin Anthropic Claude dil modeli saglar.",
        Parameters:
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true, HelpText: "anthropicApi tipli credential seçin."),
            new NodeParameterDefinition("model", "Model", NodeParameterType.String, DefaultValue: "claude-3-5-sonnet-20241022")
        ],
        Tags: ["anthropic", "claude", "ai"],
        Icon: "robot",
        Color: "#d97706",
        IsAiPowered: true,
        Credentials: ["anthropicApi"],
        SubCategory: "AI Agents Model",
        Inputs: [],
        Outputs: [new NodePort("model", "Model", NodePortType.AiModel)]
    );

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context) =>
        Task.FromResult(NodeExecutionResult.Single(context.Items));
}
