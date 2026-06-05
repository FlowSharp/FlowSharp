using System.Net.Http;
using System.Text.Json.Nodes;
using FlowSharp.Application.Credentials;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Credentials;
using FlowSharp.Domain.Nodes;
using FlowSharp.Nodes.Credentials;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace FlowSharp.Nodes.Ai.Tools;

/// <summary>
/// MCP (Model Context Protocol) istemci araci. Bir AI Agent'a baglandiginda, yapilandirilan
/// uzak MCP sunucusunun (HTTP/SSE) sundugu TUM araclari (opsiyonel allow-list ile filtreli)
/// agent'a acar. Agent altina baglanmadan dogrudan calistirilirsa, sunucudaki araclari listeler
/// (baglanti dogrulama amacli). Asil arac-cagirma orkestrasyonu
/// <see cref="Agent.SemanticKernelAgentExecutor"/> icinde yapilir.
/// </summary>
public sealed class McpToolNode : NodeType, IProvidesCredentials
{
    public const string NodeKey = "tool.mcp";
    public const string CredentialTypeKey = "mcpApi";

    public IEnumerable<CredentialSchema> CredentialSchemas =>
    [
        new CredentialSchema(CredentialTypeKey, "MCP", McpConnection.CredentialFields)
    ];

    public override NodeDefinition Definition { get; } = new(
        Key: NodeKey,
        DisplayName: "MCP Client",
        Category: NodeCategory.Ai,
        Kind: NodeKind.Ai,
        Description: "Uzak bir MCP (Model Context Protocol) sunucusunun araclarini AI Agent'a acar.",
        Parameters:
        [
            new NodeParameterDefinition("serverUrl", "Server URL", NodeParameterType.Url, IsRequired: true,
                HelpText: "MCP sunucusunun HTTP/SSE endpoint'i. Ornek: https://host/mcp"),
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: false,
                HelpText: "Opsiyonel: kimlik dogrulama (Bearer token) icin mcpApi tipli credential."),
            new NodeParameterDefinition("allowedTools", "Allowed Tools", NodeParameterType.String, IsRequired: false,
                HelpText: "Opsiyonel: virgulle ayrilmis arac adlari. Bos birakilirsa tum araclar acilir.")
        ],
        Tags: ["ai", "tool", "mcp"],
        Icon: "plug",
        Color: "#7d3cff",
        Credentials: [CredentialTypeKey],
        SubCategory: "AI Tools",
        Inputs: [],
        Outputs: [new NodePort("tool", "Tool", NodePortType.AiTool)]);

    public override async Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var serverUrl = context.GetString("serverUrl");
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            return NodeExecutionResult.Single(NodeItem.From(new JsonObject { ["error"] = "serverUrl bos." }));
        }

        var httpFactory = context.Services.GetService<IHttpClientFactory>();
        var (token, headerName, headerPrefix) = await McpConnection.ResolveAuthAsync(context);
        var allowed = McpConnection.ParseAllowedTools(context.GetString("allowedTools"));

        await using var client = await McpConnection.CreateClientAsync(
            serverUrl, token, headerName, headerPrefix, httpFactory, context.CancellationToken);

        var tools = await client.ListToolsAsync(cancellationToken: context.CancellationToken);

        var list = new JsonArray();
        foreach (var tool in tools)
        {
            if (allowed is not null && !allowed.Contains(tool.Name))
            {
                continue;
            }

            list.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description
            });
        }

        return NodeExecutionResult.Single(NodeItem.From(new JsonObject
        {
            ["serverUrl"] = serverUrl,
            ["toolCount"] = list.Count,
            ["tools"] = list
        }));
    }
}
