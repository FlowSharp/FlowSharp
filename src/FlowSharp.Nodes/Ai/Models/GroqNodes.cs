using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Ai.Models;

public sealed class GroqChatNode : AiChatNodeBase
{
    protected override string Provider => "groq";
    protected override string CredentialType => "groqApi";
    protected override string DefaultModel => "llama-3.3-70b-versatile";

    public override NodeDefinition Definition { get; } = new(
        Key: "groq.chat",
        DisplayName: "Groq",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Ai,
        Description: "Groq sohbet tamamlamasi.",
        Parameters:
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true, HelpText: "groqApi tipli credential seçin."),
            SystemPromptParam,
            PromptParam,
            ModelParam("llama-3.3-70b-versatile")
        ],
        Tags: ["groq", "llama", "ai"],
        Icon: "robot",
        IsAiPowered: true,
        Color: "#f55036",
        Credentials: ["groqApi"],
        SubCategory: "AI Chat Nodes"
    );
}

public sealed class GroqChatModelNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "groq.chatmodel",
        DisplayName: "Groq Chat Model",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Ai,
        Description: "AI Agent icin Groq dil modeli saglar.",
        Parameters:
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true, HelpText: "groqApi tipli credential seçin."),
            new NodeParameterDefinition("model", "Model", NodeParameterType.String, DefaultValue: "llama-3.3-70b-versatile")
        ],
        Tags: ["groq", "llama", "ai"],
        Icon: "robot",
        Color: "#f55036",
        IsAiPowered: true,
        Credentials: ["groqApi"],
        SubCategory: "AI Agents Model",
        Inputs: [],
        Outputs: [new NodePort("model", "Model", NodePortType.AiModel)]
    );

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context) =>
        Task.FromResult(NodeExecutionResult.Single(context.Items));
}
