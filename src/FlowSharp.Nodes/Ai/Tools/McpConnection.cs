using System.Net.Http;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Credentials;
using ModelContextProtocol.Client;
using F = FlowSharp.Domain.Credentials.CredentialFieldSchema;
using CT = FlowSharp.Domain.Credentials.CredentialFieldType;

namespace FlowSharp.Nodes.Ai.Tools;

/// <summary>
/// MCP (Model Context Protocol) HTTP/SSE istemci kurulumu icin ortak yardimci.
/// Hem <see cref="McpToolNode"/> (dogrudan calistirma) hem de
/// <see cref="Agent.SemanticKernelAgentExecutor"/> (agent arac baglama) buradan baglanir.
/// </summary>
internal static class McpConnection
{
    /// <summary>mcpApi credential tipinin alanlari: opsiyonel Bearer token + ozellestirilebilir header.</summary>
    public static IReadOnlyList<F> CredentialFields =>
    [
        new F("token", "Token", CT.Secret, IsRequired: false,
            HelpText: "Opsiyonel kimlik dogrulama token'i (header degeri olarak gonderilir)."),
        new F("headerName", "Header Name", CT.String, DefaultValue: "Authorization"),
        new F("headerPrefix", "Header Prefix", CT.String, DefaultValue: "Bearer ")
    ];

    /// <summary>allowedTools parametresini (virgulle ayrilmis) bir kumeye cevirir. Bos ise null.</summary>
    public static IReadOnlySet<string>? ParseAllowedTools(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return null;
        }

        var set = csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return set.Count > 0 ? set : null;
    }

    /// <summary>Secili mcpApi credential'indan token ve header ayarlarini cozer (yoksa null token).</summary>
    public static async Task<(string? Token, string HeaderName, string HeaderPrefix)> ResolveAuthAsync(
        INodeExecutionContext context, int itemIndex = 0)
    {
        var credName = context.GetString("_credential", itemIndex);
        if (string.IsNullOrWhiteSpace(credName))
        {
            return (null, "Authorization", "Bearer ");
        }

        var token = await context.GetCredentialAsync(McpToolNode.CredentialTypeKey, credName, "token");
        var headerName = await context.GetCredentialAsync(McpToolNode.CredentialTypeKey, credName, "headerName");
        var headerPrefix = await context.GetCredentialAsync(McpToolNode.CredentialTypeKey, credName, "headerPrefix");

        return (
            token,
            string.IsNullOrWhiteSpace(headerName) ? "Authorization" : headerName,
            headerPrefix ?? "Bearer ");
    }

    /// <summary>
    /// Verilen URL ve opsiyonel auth ile HTTP/SSE transport uzerinden bir MCP istemcisi olusturur.
    /// Donen istemci <c>IAsyncDisposable</c>'dir; cagiran tarafin dispose etmesi gerekir.
    /// </summary>
    public static async Task<McpClient> CreateClientAsync(
        string serverUrl,
        string? token,
        string headerName,
        string headerPrefix,
        IHttpClientFactory? httpClientFactory,
        CancellationToken cancellationToken)
    {
        var options = new HttpClientTransportOptions
        {
            Name = "FlowSharp",
            Endpoint = new Uri(serverUrl)
        };

        if (!string.IsNullOrWhiteSpace(token))
        {
            options.AdditionalHeaders = new Dictionary<string, string>
            {
                [headerName] = $"{headerPrefix}{token}"
            };
        }

        // Mevcut "workflow-nodes" HttpClient yapilandirmasini (timeout/proxy) yeniden kullan.
        var httpClient = httpClientFactory?.CreateClient("workflow-nodes");
        var transport = httpClient is null
            ? new HttpClientTransport(options)
            : new HttpClientTransport(options, httpClient);

        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }
}
