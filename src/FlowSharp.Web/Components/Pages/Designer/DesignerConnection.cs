namespace FlowSharp.Web.Components.Pages;

/// <summary>Tasarimci canvas'indaki iki port arasindaki baglanti (gorunum modeli).</summary>
internal sealed record DesignerConnection(string FromId, int FromPort, string ToId, int ToPort);
