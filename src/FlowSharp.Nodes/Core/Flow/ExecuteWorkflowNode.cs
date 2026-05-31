using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using FlowSharp.Application.Abstractions;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Core.Flow;

/// <summary>
/// Baska bir workflow'u (alt-workflow) senkron calistirir ve sonucunu bu akisa dondurur.
/// Hedef workflow'da bir "Execute Workflow Trigger" node'u olmalidir; gelen item'lar
/// trigger payload'i olarak aktarilir.
/// </summary>
public sealed class ExecuteWorkflowNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "flow.executeWorkflow",
        DisplayName: "Execute Workflow",
        Category: NodeCategory.Core,
        Kind: NodeKind.Action,
        Description: "Bir alt-workflow'u calistirir ve ciktisini doner.",
        Parameters:
        [
            new NodeParameterDefinition("workflowId", "Workflow ID", NodeParameterType.String, IsRequired: true,
                HelpText: "Calistirilacak workflow'un GUID'i.")
        ],
        Tags: ["core", "flow"],
        Icon: "play",
        Color: "#7d7d87");

    public override async Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var idText = context.GetString("workflowId");
        if (!Guid.TryParse(idText, out var workflowId))
        {
            return NodeExecutionResult.Failure("Gecerli bir Workflow ID (GUID) gerekli.");
        }

        // Sonsuz alt-workflow zincirini onle (recursion guard).
        const int maxDepth = 10;
        var depth = context.Trigger?["_depth"] is JsonValue dv && dv.TryGetValue<int>(out var d) ? d : 0;
        if (depth >= maxDepth)
        {
            return NodeExecutionResult.Failure($"Execute Workflow azami derinlige ({maxDepth}) ulasti; olasi sonsuz dongu engellendi.");
        }

        // Alt-workflow icin gelen item'lari payload'a koy.
        var itemsArray = new JsonArray();
        foreach (var item in context.Items)
        {
            itemsArray.Add(item.Json.DeepClone());
        }

        var payloadJson = new JsonObject
        {
            ["source"] = "subworkflow",
            ["_depth"] = depth + 1,
            ["items"] = itemsArray
        }.ToJsonString();

        // Re-entrancy icin yeni bir DI scope'unda calistir (ayri DbContext).
        using var scope = context.Services.CreateScope();
        var runner = scope.ServiceProvider.GetService<IWorkflowRunner>()
            ?? throw new InvalidOperationException("WorkflowRunner cozumlenemedi.");

        using var payload = JsonDocument.Parse(payloadJson);
        var result = await runner.ExecuteNowAsync(workflowId, payload, context.CancellationToken);

        if (!result.Succeeded)
        {
            return NodeExecutionResult.Failure(result.Error ?? "Alt-workflow basarisiz oldu.");
        }

        return NodeExecutionResult.Single(NodeItem.From(new JsonObject
        {
            ["workflowId"] = workflowId.ToString(),
            ["result"] = result.Output.DeepClone()
        }));
    }
}
