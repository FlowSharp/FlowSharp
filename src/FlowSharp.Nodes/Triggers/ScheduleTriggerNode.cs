using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Triggers
{
    /// <summary>Zamanlanmis tetikleyici. "cron" parametresiyle belirtilen periyotta calisir.</summary>
    public sealed class ScheduleTriggerNode : NodeType
    {
        public override NodeDefinition Definition { get; } = new(
            Key: "schedule.trigger",
            DisplayName: "Schedule Trigger",
            Category: NodeCategory.Trigger,
            Kind: NodeKind.Trigger,
            Description: "Workflow'u cron ifadesine gore periyodik calistirir.",
            Parameters:
            [
                new NodeParameterDefinition("cron", "Cron", NodeParameterType.String, IsRequired: true,
                DefaultValue: "*/5 * * * *", HelpText: "Ornek: */5 * * * * (her 5 dakikada bir)")
            ],
            Tags: ["trigger"],
            Icon: "clock",
            Color: "#7d7d87");

        public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
        {
            var items = context.Items.Count > 0 ? context.Items : [NodeItem.Empty()];
            return Task.FromResult(NodeExecutionResult.Single(items));
        }
    }

}
