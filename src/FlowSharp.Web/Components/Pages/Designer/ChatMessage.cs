namespace FlowSharp.Web.Components.Pages;

/// <summary>Tasarimci sohbet panelindeki tek mesaj (kullanici/bot) gorunum modeli.</summary>
internal sealed class ChatMessage(bool isUser, string text)
{
    public bool IsUser { get; } = isUser;
    public string Text { get; set; } = text;
}
