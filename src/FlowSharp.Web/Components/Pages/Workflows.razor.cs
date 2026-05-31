using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FlowSharp.Application.Abstractions;
using FlowSharp.Domain.Workflows;
using FlowSharp.Infrastructure.Data;

namespace FlowSharp.Web.Components.Pages;

public partial class Workflows
{
    [Inject] public ApplicationDbContext DbContext { get; set; } = default!;
    [Inject] public IWorkflowQueue Queue { get; set; } = default!;
    [Inject] public IWebhookRegistrar WebhookRegistrar { get; set; } = default!;
    [Inject] public NavigationManager Navigation { get; set; } = default!;

    private List<Workflow>? workflows;
    private string? message;

    protected override async Task OnInitializedAsync() => await ReloadAsync();

    private async Task ReloadAsync()
    {
        workflows = await DbContext.Workflows
            .OrderByDescending(w => w.CreatedAt)
            .AsNoTracking()
            .ToListAsync();
    }

    private async Task RunAsync(Guid id)
    {
        await Queue.EnqueueAsync(id, JsonDocument.Parse("""{"source":"manual"}"""));
        await Flash("Workflow kuyruga alindi.");
    }

    private async Task DeleteAsync(Guid id)
    {
        await WebhookRegistrar.SyncAsync(id, JsonDocument.Parse("""{"nodes":[]}""").RootElement, false);
        await DbContext.Workflows.Where(w => w.Id == id).ExecuteDeleteAsync();
        await ReloadAsync();
        await Flash("Workflow silindi.");
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
