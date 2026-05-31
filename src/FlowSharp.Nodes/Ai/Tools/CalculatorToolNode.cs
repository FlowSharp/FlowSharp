using Jint;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;
using System.Text.Json.Nodes;

namespace FlowSharp.Nodes.Ai.Tools
{

    /// <summary>Hesap makinesi araci. Agent bir matematik ifadesi vererek cagirir.</summary>
    public sealed class CalculatorToolNode : NodeType
    {
        public override NodeDefinition Definition { get; } = new(
            Key: "tool.calculator",
            DisplayName: "Calculator",
            Category: NodeCategory.Ai,
            Kind: NodeKind.Ai,
            Description: "Bir matematik ifadesini hesaplar. Girdi: hesaplanacak ifade (orn. '2*(3+4)').",
            Parameters: [],
            Tags: ["ai", "tool"],
            Icon: "calculator",
            Color: "#7d3cff",
            Inputs: [],
            Outputs: [new NodePort("tool", "Tool", NodePortType.AiTool)]);

        public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
        {
            var expression = context.Items.FirstOrDefault()?.Json["input"]?.ToString() ?? "";
            try
            {
                using var engine = new Engine(o => o.TimeoutInterval(TimeSpan.FromSeconds(2)));
                var result = engine.Evaluate(expression).ToString();
                return Task.FromResult(NodeExecutionResult.Single(NodeItem.From(new JsonObject { ["result"] = result })));
            }
            catch (Exception ex)
            {
                return Task.FromResult(NodeExecutionResult.Single(NodeItem.From(new JsonObject { ["error"] = ex.Message })));
            }
        }
    }
}
