using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Ai.Models;

public sealed class MistralChatNode : AiChatNodeBase
{
    protected override string Provider => "mistral";
    protected override string CredentialType => "mistralApi";
    protected override string DefaultModel => "mistral-large-latest";

    public override NodeDefinition Definition { get; } = new(
        Key: "mistral.chat",
        DisplayName: "Mistral AI",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Ai,
        Description: "Mistral AI sohbet tamamlamasi.",
        Parameters:
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true, HelpText: "mistralApi tipli credential seçin."),
            PromptParam,
            ModelParam("mistral-large-latest")
        ],
        Tags: ["mistral", "ai"],
        Icon: "robot",
        IsAiPowered: true,
        Color: "#f472b6",
        Credentials: ["mistralApi"],
        SubCategory: "AI Chat Nodes"
    );
}

public sealed class MistralChatModelNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "mistral.chatmodel",
        DisplayName: "Mistral Chat Model",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Ai,
        Description: "AI Agent icin Mistral AI dil modeli saglar.",
        Parameters:
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true, HelpText: "mistralApi tipli credential seçin."),
            new NodeParameterDefinition("model", "Model", NodeParameterType.String, DefaultValue: "mistral-large-latest")
        ],
        Tags: ["mistral", "ai"],
        Icon: "robot",
        Color: "#f472b6",
        IsAiPowered: true,
        Credentials: ["mistralApi"],
        SubCategory: "AI Models",
        Inputs: [],
        Outputs: [new NodePort("model", "Model", NodePortType.AiModel)]
    );

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context) =>
        Task.FromResult(NodeExecutionResult.Single(context.Items));
}
