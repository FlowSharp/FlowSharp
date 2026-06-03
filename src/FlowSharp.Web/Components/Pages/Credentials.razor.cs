using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using FlowSharp.Application.Abstractions;
using FlowSharp.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FlowSharp.Web.Components.Pages;

public partial class Credentials
{
    [Inject] public ICredentialStore CredentialStore { get; set; } = default!;
    [Inject] public AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    [Inject] public ApplicationDbContext DbContext { get; set; } = default!;
    [Inject] public FlowSharp.Web.Services.IUiNotifier Notifier { get; set; } = default!;

    private IReadOnlyList<CredentialSummary>? items;
    private bool editing;
    private string? message;
    private CredentialForm form = new();
    private readonly List<FieldRow> fields = [];
    private string? currentUserId;
    private bool isAdmin;
    private Dictionary<string, string> ownerEmails = [];

    // Admin gorunumu: kendi credential'lari ust tabloda, digerleri alt tabloda.
    private IEnumerable<CredentialSummary> MineCredentials => items?.Where(c => c.OwnerId == currentUserId) ?? [];
    private IEnumerable<CredentialSummary> OtherCredentials => items?.Where(c => c.OwnerId != currentUserId) ?? [];

    // CredentialsManage yetkisi olan herkes erisir. Admin tum credential'lari yonetir (ownerId: null);
    // Editor/Member yalniz kendi kayitlarini gorur/yonetir (owner-scope). Yeni kayit olusturan kullaniciya atanir.
    protected override async Task OnInitializedAsync()
    {
        (currentUserId, isAdmin) = await FlowSharp.Web.Security.CurrentUser.ResolveAsync(AuthenticationStateProvider);
        await ReloadAsync();
    }

    /// <summary>Admin ise kisit yok (null), degilse yalniz kendi sahipligi.</summary>
    private string? ScopeOwnerId => isAdmin ? null : currentUserId;

    private async Task ReloadAsync()
    {
        items = await CredentialStore.ListAsync(ScopeOwnerId);

        var ownerIds = items.Where(c => c.OwnerId != null).Select(c => c.OwnerId!).Distinct().ToList();
        ownerEmails = await DbContext.Users
            .Where(u => ownerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? u.UserName ?? u.Id);
    }

    /// <summary>Credential sahibinin gosterilecek etiketi (e-posta); sahipsizse "Sistem".</summary>
    private string OwnerLabel(CredentialSummary c) =>
        c.OwnerId is { } id && ownerEmails.TryGetValue(id, out var email) ? email : L["common.system"];

    private void StartCreate()
    {
        form = new CredentialForm();
        fields.Clear();
        fields.Add(new FieldRow { Key = "apiKey" });
        editing = true;
        message = null;
    }

    private async Task EditAsync(Guid id)
    {
        var detail = await CredentialStore.GetAsync(id, ScopeOwnerId);
        if (detail is null) return;
        form = new CredentialForm { Id = detail.Id, Name = detail.Name, Type = detail.Type };
        fields.Clear();
        foreach (var pair in detail.Data)
        {
            fields.Add(new FieldRow { Key = pair.Key, Value = pair.Value });
        }
        editing = true;
        message = null;
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(form.Name) || string.IsNullOrWhiteSpace(form.Type))
        {
            message = "Ad ve tip zorunlu.";
            return;
        }
        var data = fields.Where(f => !string.IsNullOrWhiteSpace(f.Key))
            .ToDictionary(f => f.Key, f => f.Value ?? string.Empty);
        // Yeni kayit olusturan admin'e atanir; guncellemede sahip degismez (store korur).
        await CredentialStore.SaveAsync(new CredentialInput(form.Id, form.Name, form.Type, data, currentUserId));
        editing = false;
        await ReloadAsync();
        Notifier.Success(L["credentials.msg.saved"]);
    }

    private async Task DeleteAsync(Guid id)
    {
        var name = items?.FirstOrDefault(c => c.Id == id)?.Name ?? "";
        if (!await Notifier.ConfirmDeleteAsync(name))
        {
            return;
        }

        await CredentialStore.DeleteAsync(id, ScopeOwnerId);
        await ReloadAsync();
        Notifier.Success(L["credentials.msg.deleted"]);
    }

    private void Cancel() => editing = false;

    private sealed class CredentialForm
    {
        public Guid? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }

    private sealed class FieldRow
    {
        public string Key { get; set; } = string.Empty;
        public string? Value { get; set; }
    }
}
