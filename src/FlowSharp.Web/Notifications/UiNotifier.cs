using MudBlazor;
using FlowSharp.Web.Localization;

namespace FlowSharp.Web.Notifications;

/// <summary>
/// Merkezi UI bildirim ve onay servisi. Toast (snackbar) ve onay dialog'larini tek bir
/// yerden yonetir; boylece sayfalar dogrudan ISnackbar/IDialogService'e bagimli olmaz ve
/// tum bildirimler tutarli olur.
/// </summary>
public interface IUiNotifier
{
    void Success(string message);
    void Info(string message);
    void Warning(string message);
    void Error(string message);

    /// <summary>Onay dialog'u gosterir. Kullanici onaylarsa <c>true</c> doner.</summary>
    Task<bool> ConfirmAsync(string title, string message, string? confirmText = null, string? cancelText = null);

    /// <summary>Yikici (geri alinamaz) bir islem icin kirmizi onay butonlu dialog.</summary>
    Task<bool> ConfirmDeleteAsync(string itemName);
}

public sealed class UiNotifier(ISnackbar snackbar, IDialogService dialog, ILocalizer l) : IUiNotifier
{
    public void Success(string message) => snackbar.Add(message, Severity.Success);

    public void Info(string message) => snackbar.Add(message, Severity.Info);

    public void Warning(string message) => snackbar.Add(message, Severity.Warning);

    public void Error(string message) => snackbar.Add(message, Severity.Error);

    public async Task<bool> ConfirmAsync(string title, string message, string? confirmText = null, string? cancelText = null)
    {
        var result = await dialog.ShowMessageBoxAsync(
            title,
            message,
            yesText: confirmText ?? l["common.confirm"],
            cancelText: cancelText ?? l["common.cancel"]);
        return result == true;
    }

    public async Task<bool> ConfirmDeleteAsync(string itemName)
    {
        var result = await dialog.ShowMessageBoxAsync(new MessageBoxOptions
        {
            Title = l["common.confirm_delete_title"],
            Message = string.Format(l["common.confirm_delete_message"], itemName),
            YesText = l["common.delete"],
            CancelText = l["common.cancel"]
        }, new DialogOptions { CloseButton = true });
        return result == true;
    }
}
