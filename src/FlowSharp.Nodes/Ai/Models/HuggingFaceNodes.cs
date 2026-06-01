using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Ai.Models;

public sealed class HuggingFaceChatNode : AiChatNodeBase
{
    protected override string Provider => "huggingface";
    protected override string CredentialType => "huggingFaceApi";
    protected override string DefaultModel => "meta-llama/Llama-3.3-70B-Instruct";

    public override NodeDefinition Definition { get; } = new(
        Key: "huggingface.chat",
        DisplayName: "Hugging Face",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Ai,
        Description: "Hugging Face Serverless Inference API sohbet tamamlamasi.",
        Parameters:
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true, HelpText: "huggingFaceApi tipli credential seçin."),
            PromptParam,
            ModelParam("meta-llama/Llama-3.3-70B-Instruct")
        ],
        Tags: ["huggingface", "ai"],
        Icon: "robot",
        IsAiPowered: true,
        Color: "#eab308",
        Credentials: ["huggingFaceApi"],
        SubCategory: "AI Chat Nodes"
    );
}

public sealed class HuggingFaceChatModelNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "huggingface.chatmodel",
        DisplayName: "Hugging Face Chat Model",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Ai,
        Description: "AI Agent icin Hugging Face dil modeli saglar.",
        Parameters:
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true, HelpText: "huggingFaceApi tipli credential seçin."),
            new NodeParameterDefinition("model", "Model", NodeParameterType.String, DefaultValue: "meta-llama/Llama-3.3-70B-Instruct")
        ],
        Tags: ["huggingface", "ai"],
        Icon: "robot",
        Color: "#eab308",
        IsAiPowered: true,
        Credentials: ["huggingFaceApi"],
        SubCategory: "AI Models",
        Inputs: [],
        Outputs: [new NodePort("model", "Model", NodePortType.AiModel)]
    );

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context) =>
        Task.FromResult(NodeExecutionResult.Single(context.Items));
}
