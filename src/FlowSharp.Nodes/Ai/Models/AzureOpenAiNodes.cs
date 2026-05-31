using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Ai.Models;

public sealed class AzureOpenAiChatNode : AiChatNodeBase
{
    protected override string Provider => "azureopenai";
    protected override string CredentialType => "azureOpenAiApi";
    protected override string DefaultModel => "gpt-4o-mini";

    public override NodeDefinition Definition { get; } = new(
        Key: "azureopenai.chat",
        DisplayName: "Azure OpenAI",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Ai,
        Description: "Azure OpenAI sohbet tamamlamasi.",
        Parameters:
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true, HelpText: "azureOpenAiApi tipli credential seçin."),
            PromptParam,
            new NodeParameterDefinition("model", "Deployment Name", NodeParameterType.String, HelpText: "Azure deployment adi (bos birakilirsa credential'daki deploymentName kullanilir).")
        ],
        Tags: ["azure", "openai", "ai"],
        Icon: "bot",
        IsAiPowered: true,
        Color: "#0078d4",
        Credentials: ["azureOpenAiApi"]
    );
}

public sealed class AzureOpenAiChatModelNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "azureopenai.chatmodel",
        DisplayName: "Azure OpenAI Chat Model",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Ai,
        Description: "AI Agent icin Azure OpenAI dil modeli saglar.",
        Parameters:
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true, HelpText: "azureOpenAiApi tipli credential seçin."),
            new NodeParameterDefinition("model", "Deployment Name", NodeParameterType.String, HelpText: "Azure deployment adi (bos ise credential'daki deploymentName kullanilir).")
        ],
        Tags: ["azure", "openai", "ai"],
        Icon: "bot",
        Color: "#0078d4",
        IsAiPowered: true,
        Credentials: ["azureOpenAiApi"],
        Inputs: [],
        Outputs: [new NodePort("model", "Model", NodePortType.AiModel)]
    );

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context) =>
        Task.FromResult(NodeExecutionResult.Single(context.Items));
}
