using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Ai.Agent;

/// <summary>
/// AI Agent. Altina baglanan Model / Tool / Memory alt-node'larini kullanir.
/// Motor bu node'u WorkflowExecutionEngine icinde ozel tool-calling dongusu ile calistirir.
/// </summary>
public sealed class AiAgentNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "ai.agent",
        DisplayName: "AI Agent",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Ai,
        Description: "Araclari kullanabilen bir AI ajan adimi.",
        Parameters:
        [
            new NodeParameterDefinition("systemPrompt", "System Prompt", NodeParameterType.Text,
                DefaultValue: "Sen yardimci bir asistansin.", HelpText: "Ajanin rolu/talimatlari."),
            new NodeParameterDefinition("text", "User Input", NodeParameterType.Text, IsRequired: true,
                DefaultValue: "{{$json.text}}", HelpText: "Kullanici mesaji / gorev.")
        ],
        Tags: ["ai"],
        Icon: "robot",
        Color: "#10a37f",
        IsAiPowered: true,
        SubCategory: "AI Agents",
        Inputs:
        [
            NodePort.Main,
            new NodePort("model", "Model", NodePortType.AiModel),
            new NodePort("tool", "Tool", NodePortType.AiTool),
            new NodePort("memory", "Memory", NodePortType.AiMemory)
        ],
        Outputs: [NodePort.Main]);

    // Motor agent'i ozel calistirir; bu yalniz dogrudan baglanmadan calistirilmaya calisilirsa devreye girer.
    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context) =>
        Task.FromResult(NodeExecutionResult.Failure("AI Agent bir Model alt-node'una baglanmali."));
}
