using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using FlowSharp.Application.Errors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FlowSharp.Application.Abstractions;
using FlowSharp.Domain.Security;
using FlowSharp.Domain.Workflows;
using FlowSharp.Infrastructure.Data;

namespace FlowSharp.Web.Components.Pages;

public partial class Workflows
{
    [Inject] public ApplicationDbContext DbContext { get; set; } = default!;
    [Inject] public IWorkflowQueue Queue { get; set; } = default!;
    [Inject] public IWorkflowRunner Runner { get; set; } = default!;
    [Inject] public IWebhookRegistrar WebhookRegistrar { get; set; } = default!;
    [Inject] public NavigationManager Navigation { get; set; } = default!;
    [Inject] public IAuthorizationService AuthorizationService { get; set; } = default!;
    [Inject] public AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    [Inject] public FlowSharp.Web.Services.IUiNotifier Notifier { get; set; } = default!;

    private List<Workflow>? workflows;
    private bool canWrite;
    private bool canExecute;
    private Guid? runningId;
    private string? currentUserId;
    private bool isAdmin;
    private Dictionary<string, string> ownerEmails = [];

    // Admin gorunumu: kendi kayitlari ust tabloda, digerleri alt tabloda.
    private IEnumerable<Workflow> MineWorkflows => workflows?.Where(w => w.OwnerId == currentUserId) ?? [];
    private IEnumerable<Workflow> OtherWorkflows => workflows?.Where(w => w.OwnerId != currentUserId) ?? [];

    protected override async Task OnInitializedAsync()
    {
        await LoadPermissionsAsync();
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var query = DbContext.Workflows.AsNoTracking().AsQueryable();
        if (!isAdmin)
        {
            query = query.Where(w => w.OwnerId == currentUserId);
        }

        workflows = await query
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync();

        // Admin tum kayitlari gordugunden, sahip e-postalarini cozup gosterelim.
        if (isAdmin)
        {
            var ownerIds = workflows.Where(w => w.OwnerId != null).Select(w => w.OwnerId!).Distinct().ToList();
            ownerEmails = await DbContext.Users
                .Where(u => ownerIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Email ?? u.UserName ?? u.Id);
        }
    }

    /// <summary>Workflow sahibinin gosterilecek etiketi (e-posta); sahipsizse "Sistem".</summary>
    private string OwnerLabel(Workflow wf) =>
        wf.OwnerId is { } id && ownerEmails.TryGetValue(id, out var email) ? email : L["common.system"];

    private async Task RunAsync(Guid id)
    {
        if (!canExecute || !await OwnsAsync(id))
        {
            Notifier.Error(L["common.no_permission"]);
            return;
        }

        runningId = id;
        StateHasChanged();
        try
        {
            var result = await Runner.ExecuteNowAsync(id, JsonDocument.Parse("""{"source":"manual"}"""));
            if (result.Succeeded)
            {
                Notifier.Success(L["workflows.msg.run_ok"]);
            }
            else
            {
                Notifier.Error(string.Format(L["workflows.msg.run_failed"], result.Error));
            }
        }
        catch (Exception ex)
        {
            Notifier.Error(string.Format(L["workflows.msg.run_failed"], ex.ToUserMessage()));
        }
        finally
        {
            runningId = null;
            StateHasChanged();
        }
    }

    private async Task DeleteAsync(Guid id)
    {
        if (!canWrite || !await OwnsAsync(id))
        {
            Notifier.Error(L["common.no_permission"]);
            return;
        }

        var name = workflows?.FirstOrDefault(w => w.Id == id)?.Name ?? "";
        if (!await Notifier.ConfirmDeleteAsync(name))
        {
            return;
        }

        await WebhookRegistrar.SyncAsync(id, JsonDocument.Parse("""{"nodes":[]}""").RootElement, false);
        await DbContext.Workflows.Where(w => w.Id == id).ExecuteDeleteAsync();
        await ReloadAsync();
        Notifier.Success(L["workflows.msg.deleted"]);
    }

    private async Task LoadPermissionsAsync()
    {
        var user = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
        canWrite = (await AuthorizationService.AuthorizeAsync(user, AppPermissions.WorkflowsWrite)).Succeeded;
        canExecute = (await AuthorizationService.AuthorizeAsync(user, AppPermissions.WorkflowsExecute)).Succeeded;
        (currentUserId, isAdmin) = await FlowSharp.Web.Security.CurrentUser.ResolveAsync(AuthenticationStateProvider);
    }

    /// <summary>Verilen workflow oturum sahibine (ya da Admin'e) ait mi? Sahiplik disi erisimi engeller.</summary>
    private async Task<bool> OwnsAsync(Guid id) =>
        isAdmin || await DbContext.Workflows.AnyAsync(w => w.Id == id && w.OwnerId == currentUserId);
}
