namespace FlowSharp.Application.Workflows;

/// <summary>
/// Bir workflow sahibinin dakikalik calistirma limiti asildiginda firlatilir. Cagiranlar
/// (UI / webhook) bunu yakalayip kendi baglamlarina uygun yanit uretir (kullanici mesaji / 200 ignored).
/// </summary>
public sealed class WorkflowRateLimitedException(string message) : Exception(message);
