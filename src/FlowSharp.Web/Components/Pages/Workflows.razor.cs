using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
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

    private List<Workflow>? workflows;
    private string? message;
    private bool canWrite;
    private bool canExecute;
    private Guid? runningId;

    protected override async Task OnInitializedAsync()
    {
        await LoadPermissionsAsync();
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        workflows = await DbContext.Workflows
            .OrderByDescending(w => w.CreatedAt)
            .AsNoTracking()
            .ToListAsync();
    }

    private async Task RunAsync(Guid id)
    {
        if (!canExecute)
        {
            await Flash("Workflow calistirma yetkiniz yok.");
            return;
        }

        runningId = id;
        StateHasChanged();
        try
        {
            var result = await Runner.ExecuteNowAsync(id, JsonDocument.Parse("""{"source":"manual"}"""));
            await Flash(result.Succeeded ? "Workflow basariyla calisti." : $"Hata: {result.Error}");
        }
        catch (Exception ex)
        {
            await Flash($"Hata: {ex.Message}");
        }
        finally
        {
            runningId = null;
            StateHasChanged();
        }
    }

    private async Task DeleteAsync(Guid id)
    {
        if (!canWrite)
        {
            await Flash("Workflow silme yetkiniz yok.");
            return;
        }

        await WebhookRegistrar.SyncAsync(id, JsonDocument.Parse("""{"nodes":[]}""").RootElement, false);
        await DbContext.Workflows.Where(w => w.Id == id).ExecuteDeleteAsync();
        await ReloadAsync();
        await Flash("Workflow silindi.");
    }

    private async Task LoadPermissionsAsync()
    {
        var user = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
        canWrite = (await AuthorizationService.AuthorizeAsync(user, AppPermissions.WorkflowsWrite)).Succeeded;
        canExecute = (await AuthorizationService.AuthorizeAsync(user, AppPermissions.WorkflowsExecute)).Succeeded;
    }

    private async Task Flash(string text)
    {
        message = text;
        StateHasChanged();
        await Task.Delay(2400);
        message = null;
        StateHasChanged();
    }
}
