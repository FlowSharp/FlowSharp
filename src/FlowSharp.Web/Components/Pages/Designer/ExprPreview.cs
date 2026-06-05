namespace FlowSharp.Web.Components.Pages;

/// <summary>
/// Bir parametre ifadesinin ({{ ... }}) canli onizleme durumu: yok / gecerli (yesil) / gecersiz (kirmizi).
/// </summary>
internal sealed record ExprPreview(string State, string Message)
{
    public static readonly ExprPreview None = new("none", "");
    public static ExprPreview Valid(string preview) => new("valid", preview);
    public static ExprPreview Invalid(string message) => new("invalid", message);
}
