using System.Text.Json.Nodes;

namespace FlowSharp.Application.Workflows;

/// <summary>Bir workflow calismasinin genel sonucu ve node bazli calisma gunlugu.</summary>
public sealed record WorkflowRunResult(
    bool Succeeded,
    string? Error,
    JsonNode Output,
    IReadOnlyList<NodeRunData> Nodes);

/// <summary>Tek bir node'un calisma kaydi (izleme ekrani ve SignalR icin).</summary>
public sealed record NodeRunData(
    string NodeId,
    string NodeName,
    string NodeType,
    NodeRunStatus Status,
    JsonNode Output,
    string? Error,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int ItemCount);

public enum NodeRunStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Skipped = 4
}
