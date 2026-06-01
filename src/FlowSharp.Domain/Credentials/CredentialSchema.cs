namespace FlowSharp.Domain.Credentials;

/// <summary>Bir credential alaninin UI'da nasil render edilecegini belirler.</summary>
public enum CredentialFieldType
{
    String = 0,
    Secret = 1,
    Boolean = 2,
    Number = 3
}

/// <summary>
/// Tek bir credential alani (orn. Postgres icin <c>host</c>, <c>password</c>).
/// UI bu tanima gore input/checkbox/password render eder; isimden tahmin yapilmaz.
/// </summary>
public sealed record CredentialFieldSchema(
    string Key,
    string Label,
    CredentialFieldType Type = CredentialFieldType.String,
    bool IsRequired = false,
    string? DefaultValue = null,
    string? Placeholder = null,
    string? HelpText = null);

/// <summary>
/// Bir credential tipinin (orn. <c>postgres</c>) tam alan semasi. Node'lar
/// <see cref="FlowSharp.Domain.Nodes.NodeDefinition.Credentials"/> ile bu tipe baglanir;
/// UI alanlari buradan dinamik olarak uretir.
/// </summary>
public sealed record CredentialSchema(
    string Type,
    string DisplayName,
    IReadOnlyList<CredentialFieldSchema> Fields);
