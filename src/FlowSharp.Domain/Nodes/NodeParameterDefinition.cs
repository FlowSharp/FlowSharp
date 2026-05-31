namespace FlowSharp.Domain.Nodes;

public sealed record NodeParameterDefinition(
    string Key,
    string Label,
    NodeParameterType Type,
    bool IsRequired = false,
    string? DefaultValue = null,
    IReadOnlyList<string>? Options = null,
    string? HelpText = null);
