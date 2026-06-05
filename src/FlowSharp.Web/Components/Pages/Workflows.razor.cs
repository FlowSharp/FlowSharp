using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authorization;
using FlowSharp.Application.Errors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FlowSharp.Application.Identity;
using FlowSharp.Application.Security;
using FlowSharp.Application.Workflows;
using FlowSharp.Domain.Security;
using FlowSharp.Domain.Workflows;

namespace FlowSharp.Web.Components.Pages;

public partial class Workflows
{
    [Inject] public IWorkflowService WorkflowService { get; set; } = default!;
    [Inject] public IUserDirectory UserDirectory { get; set; } = default!;
    [Inject] public NavigationManager Navigation { get; set; } = default!;
    [Inject] public IAuthorizationService AuthorizationService { get; set; } = default!;
    [Inject] public AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    [Inject] public FlowSharp.Web.Notifications.IUiNotifier Notifier { get; set; } = default!;

    private IReadOnlyList<Workflow>? workflows;
    private bool canWrite;
    private bool canExecute;
    private Guid? runningId;
    private ActorScope actor;
    private bool isAdmin => actor.IsAdmin;
    private IReadOnlyDictionary<string, string> ownerEmails = new Dictionary<string, string>();

    // Admin gorunumu: kendi kayitlari ust tabloda, digerleri alt tabloda.
    private IEnumerable<Workflow> MineWorkflows => workflows?.Where(w => w.OwnerId == actor.UserId) ?? [];
    private IEnumerable<Workflow> OtherWorkflows => workflows?.Where(w => w.OwnerId != actor.UserId) ?? [];

    protected override async Task OnInitializedAsync()
    {
        await LoadPermissionsAsync();
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        workflows = await WorkflowService.ListAsync(actor);

        // Admin tum kayitlari gordugunden, sahip e-postalarini cozup gosterelim.
        if (actor.IsAdmin)
        {
            var ownerIds = workflows.Where(w => w.OwnerId != null).Select(w => w.OwnerId!);
            ownerEmails = await UserDirectory.GetEmailsAsync(ownerIds);
        }
    }

    /// <summary>Workflow sahibinin gosterilecek etiketi (e-posta); sahipsizse "Sistem".</summary>
    private string OwnerLabel(Workflow wf) =>
        wf.OwnerId is { } id && ownerEmails.TryGetValue(id, out var email) ? email : L["common.system"];

    private async Task RunAsync(Guid id)
    {
        if (!canExecute || !await WorkflowService.OwnsAsync(id, actor))
        {
            Notifier.Error(L["common.no_permission"]);
            return;
        }

        runningId = id;
        StateHasChanged();
        try
        {
            var result = await WorkflowService.RunAsync(id, JsonDocument.Parse("""{"source":"manual"}"""), actor);
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
        if (!canWrite || !await WorkflowService.OwnsAsync(id, actor))
        {
            Notifier.Error(L["common.no_permission"]);
            return;
        }

        var name = workflows?.FirstOrDefault(w => w.Id == id)?.Name ?? "";
        if (!await Notifier.ConfirmDeleteAsync(name))
        {
            return;
        }

        await WorkflowService.DeleteAsync(id, actor);
        await ReloadAsync();
        Notifier.Success(L["workflows.msg.deleted"]);
    }

    private async Task LoadPermissionsAsync()
    {
        var user = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
        canWrite = (await AuthorizationService.AuthorizeAsync(user, AppPermissions.WorkflowsWrite)).Succeeded;
        canExecute = (await AuthorizationService.AuthorizeAsync(user, AppPermissions.WorkflowsExecute)).Succeeded;
        actor = await FlowSharp.Web.Security.CurrentUser.ResolveScopeAsync(AuthenticationStateProvider);
    }
}
