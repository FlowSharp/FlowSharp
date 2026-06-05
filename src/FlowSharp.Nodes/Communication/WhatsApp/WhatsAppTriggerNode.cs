using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Communication.WhatsApp;

/// <summary>
/// WhatsApp Cloud API webhook tetikleyicisi. Meta'nin gonderdigi gelen mesaj ve durum
/// guncellemelerinde workflow'u baslatir. Web katmanindaki webhook endpoint'i Meta dogrulama
/// (GET hub.challenge) ve gelen payload normalize islemini yapar; bu node yalniz yapilandirma
/// (path/verifyToken/event) tasir ve gelen item'i gecirir.
/// </summary>
public sealed class WhatsAppTriggerNode : NodeType
{
    public const string NodeKey = "whatsapp.trigger";

    public override NodeDefinition Definition { get; } = new(
        Key: NodeKey,
        DisplayName: "WhatsApp Trigger",
        Category: NodeCategory.Trigger,
        Kind: NodeKind.Trigger,
        Description: "WhatsApp Cloud API'den gelen mesaj/durum geldiginde workflow'u baslatir.",
        Parameters:
        [
            new NodeParameterDefinition("path", "Path", NodeParameterType.String, IsRequired: true,
                DefaultValue: "whatsapp", HelpText: "Webhook URL: /webhook/{workflowKey}/{path}"),
            new NodeParameterDefinition("verifyToken", "Verify Token", NodeParameterType.String,
                HelpText: "Meta panelinde webhook kurulumunda girilen dogrulama anahtari."),
            new NodeParameterDefinition("event", "Event", NodeParameterType.Select, DefaultValue: "messages",
                Options: ["messages", "statuses", "all"],
                HelpText: "messages: yalniz gelen mesaj tetikler (onerilen). statuses: teslim/okundu. all: hepsi.")
        ],
        Tags: ["trigger", "communication"],
        Icon: "message-circle",
        Color: "#25d366");

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var items = context.Items.Count > 0 ? context.Items : [NodeItem.Empty()];
        return Task.FromResult(NodeExecutionResult.Single(items));
    }
}
