namespace FlowSharp.Domain.Nodes;

public sealed record NodeParameterDefinition(
    string Key,
    string Label,
    NodeParameterType Type,
    bool IsRequired = false,
    string? DefaultValue = null,
    IReadOnlyList<string>? Options = null,
    string? HelpText = null,
    bool DynamicOptions = false,
    ParameterCondition? ShowWhen = null,
    bool InheritsUpstream = false);

/// <summary>
/// Bir parametrenin yalniz baska bir parametre belirli degerlerden birini aldiginda
/// gosterilmesini saglar (kosullu gorunurluk). UI bu kosulu degerlendirip alani gizler/gosterir.
/// </summary>
public sealed record ParameterCondition(string Field, IReadOnlyList<string> Values);
