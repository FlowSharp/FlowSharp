using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Ai.Models;

public sealed class OpenAiChatNode : AiChatNodeBase
{
    protected override string Provider => "openai";
    protected override string CredentialType => "openAiApi";
    protected override string DefaultModel => "gpt-4o-mini";

    public override NodeDefinition Definition { get; } = new(
        Key: "openai.chat",
        DisplayName: "OpenAI",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Ai,
        Description: "OpenAI sohbet tamamlamasi.",
        Parameters:
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true, HelpText: "openAiApi tipli credential seçin."),
            SystemPromptParam,
            PromptParam,
            ModelParam("gpt-4o-mini")
        ],
        Tags: ["openai", "ai"],
        Icon: "robot",
        IsAiPowered: true,
        Color: "#10a37f",
        Credentials: ["openAiApi"],
        SubCategory: "AI Chat Nodes"
    );
}

public sealed class OpenAiChatModelNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "openai.chatmodel",
        DisplayName: "OpenAI Chat Model",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Ai,
        Description: "AI Agent icin OpenAI dil modeli saglar.",
        Parameters:
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true, HelpText: "openAiApi tipli credential seçin."),
            new NodeParameterDefinition("model", "Model", NodeParameterType.String, DefaultValue: "gpt-4o-mini")
        ],
        Tags: ["openai", "ai"],
        Icon: "robot",
        Color: "#10a37f",
        IsAiPowered: true,
        Credentials: ["openAiApi"],
        SubCategory: "AI Agents Model",
        Inputs: [],
        Outputs: [new NodePort("model", "Model", NodePortType.AiModel)]
    );

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context) =>
        Task.FromResult(NodeExecutionResult.Single(context.Items));
}
