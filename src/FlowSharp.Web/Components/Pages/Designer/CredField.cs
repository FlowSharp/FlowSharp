using FlowSharp.Domain.Credentials;

namespace FlowSharp.Web.Components.Pages;

/// <summary>
/// Node detayinda satir-ici credential olusturma formundaki tek alan. Sema'dan gelen alanlarda
/// render tipini ve etiketini tasir; serbest alanlarda varsayilan String/key kullanilir.
/// </summary>
internal sealed class CredField
{
    public string Key { get; set; } = "";
    public string? Value { get; set; }
    public string? Label { get; set; }
    public CredentialFieldType FieldType { get; set; } = CredentialFieldType.String;
    public bool FromSchema { get; set; }
}
