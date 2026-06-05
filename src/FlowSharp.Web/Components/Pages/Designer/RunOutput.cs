namespace FlowSharp.Web.Components.Pages;

/// <summary>Bir node'un editor onizlemesinde gosterilen calisma ciktisi (item sayisi, JSON, hata).</summary>
internal sealed record RunOutput(int ItemCount, string Json, string? Error = null);
