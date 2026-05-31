using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Core;

public sealed class StickyNoteNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "core.stickyNote",
        DisplayName: "Not / Grup",
        Category: NodeCategory.Core,
        Kind: NodeKind.Action,
        Description: "Akıştaki node'ları gruplamak ve açıklama yazmak için not kutusu.",
        Parameters:
        [
            new NodeParameterDefinition("title", "Başlık", NodeParameterType.String, DefaultValue: "Grup Başlığı"),
            new NodeParameterDefinition("notes", "Notlar", NodeParameterType.Text, DefaultValue: ""),
            new NodeParameterDefinition("color", "Renk", NodeParameterType.Select, DefaultValue: "green", Options: ["green", "blue", "yellow", "red", "purple"]),
            new NodeParameterDefinition("width", "Genişlik", NodeParameterType.String, DefaultValue: "400"),
            new NodeParameterDefinition("height", "Yükseklik", NodeParameterType.String, DefaultValue: "250"),
            new NodeParameterDefinition("collapsed", "Katlanmış", NodeParameterType.Boolean, DefaultValue: "false")
        ],
        Tags: ["sticky", "note", "group"],
        Icon: "sticky",
        Color: "#10b981",
        Inputs: Array.Empty<NodePort>(),
        Outputs: Array.Empty<NodePort>()
    );

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context) =>
        Task.FromResult(NodeExecutionResult.Single(context.Items));
}
