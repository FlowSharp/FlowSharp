using System.Text.Json.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Application.Nodes.Agents;

/// <summary>Bir AI agent'a bagli alt-node (Model / Tool / Memory).</summary>
public sealed record AgentSubNode(string Type, string Name, JsonObject Parameters, NodePortType PortType);

/// <summary>
/// Verilen node icin bir yurutme baglami uretir. Motor bunu saglar; boylece agent executor
/// (Nodes katmani) Infrastructure'a bagimli olmadan parametre/expression cozer ve arac calistirir.
/// </summary>
public delegate INodeExecutionContext AgentContextFactory(
    string nodeType, string nodeName, JsonObject parameters, IReadOnlyList<NodeItem> items);

/// <summary>AI agent calistirma istegi (motordan executor'a aktarilan veri).</summary>
public sealed record AgentRequest(
    string AgentType,
    string AgentName,
    JsonObject AgentParameters,
    IReadOnlyList<NodeItem> Input,
    IReadOnlyList<AgentSubNode> Subs,
    AgentContextFactory ContextFactory);

/// <summary>AI agent calistirma sonucu.</summary>
public sealed record AgentResult(bool Succeeded, NodeItem Item, string? Error)
{
    public static AgentResult Ok(NodeItem item) => new(true, item, null);
    public static AgentResult Fail(string error) => new(false, NodeItem.Empty(), error);
}

/// <summary>
/// AI Agent orkestrasyonu (model secimi, credential cozumu, arac baglama, LLM cagrisi).
/// Provider'a ozel bilgi burada (Nodes/AI katmani) yasar; generic workflow motoru bunu cagirir.
/// </summary>
public interface IAgentExecutor
{
    Task<AgentResult> ExecuteAsync(AgentRequest request, CancellationToken cancellationToken);
}
