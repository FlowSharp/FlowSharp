using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Ai.Models;

public sealed class CohereChatNode : AiChatNodeBase
{
    protected override string Provider => "cohere";
    protected override string CredentialType => "cohereApi";
    protected override string DefaultModel => "command-r-plus";

    public override NodeDefinition Definition { get; } = new(
        Key: "cohere.chat",
        DisplayName: "Cohere",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Ai,
        Description: "Cohere sohbet tamamlamasi.",
        Parameters:
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true, HelpText: "cohereApi tipli credential seçin."),
            PromptParam,
            ModelParam("command-r-plus")
        ],
        Tags: ["cohere", "ai"],
        Icon: "bot",
        IsAiPowered: true,
        Color: "#8b5cf6",
        Credentials: ["cohereApi"]
    );
}

public sealed class CohereChatModelNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "cohere.chatmodel",
        DisplayName: "Cohere Chat Model",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Ai,
        Description: "AI Agent icin Cohere dil modeli saglar.",
        Parameters:
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true, HelpText: "cohereApi tipli credential seçin."),
            new NodeParameterDefinition("model", "Model", NodeParameterType.String, DefaultValue: "command-r-plus")
        ],
        Tags: ["cohere", "ai"],
        Icon: "bot",
        Color: "#8b5cf6",
        IsAiPowered: true,
        Credentials: ["cohereApi"],
        Inputs: [],
        Outputs: [new NodePort("model", "Model", NodePortType.AiModel)]
    );

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context) =>
        Task.FromResult(NodeExecutionResult.Single(context.Items));
}
