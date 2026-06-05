using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Ai.Models;

public sealed class GoogleGeminiChatNode : AiChatNodeBase
{
    protected override string Provider => "gemini";
    protected override string CredentialType => "googleGeminiApi";
    protected override string DefaultModel => "gemini-2.5-flash";

    public override NodeDefinition Definition { get; } = new(
        Key: "gemini.chat",
        DisplayName: "Google Gemini",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Ai,
        Description: "Google Gemini sohbet tamamlamasi.",
        Parameters:
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true, HelpText: "googleGeminiApi tipli credential seçin."),
            SystemPromptParam,
            PromptParam,
            ModelParam("gemini-2.5-flash")
        ],
        Tags: ["gemini", "google", "ai"],
        Icon: "robot",
        IsAiPowered: true,
        Color: "#4285f4",
        Credentials: ["googleGeminiApi"],
        SubCategory: "AI Chat Nodes"
    );
}

public sealed class GoogleGeminiChatModelNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "gemini.chatmodel",
        DisplayName: "Google Gemini Chat Model",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Ai,
        Description: "AI Agent icin Google Gemini dil modeli saglar.",
        Parameters:
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true, HelpText: "googleGeminiApi tipli credential seçin."),
            new NodeParameterDefinition("model", "Model", NodeParameterType.String, DefaultValue: "gemini-2.5-flash")
        ],
        Tags: ["gemini", "google", "ai"],
        Icon: "robot",
        Color: "#4285f4",
        IsAiPowered: true,
        Credentials: ["googleGeminiApi"],
        SubCategory: "AI Models",
        Inputs: [],
        Outputs: [new NodePort("model", "Model", NodePortType.AiModel)]
    );

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context) =>
        Task.FromResult(NodeExecutionResult.Single(context.Items));
}
