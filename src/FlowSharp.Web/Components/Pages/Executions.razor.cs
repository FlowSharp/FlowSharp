using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlowSharp.Application.Workflows;
using FlowSharp.Domain.Workflows;

namespace FlowSharp.Web.Components.Pages;

public partial class Executions
{
    [Inject] public IExecutionService ExecutionService { get; set; } = default!;
    [Inject] public AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    private IReadOnlyList<WorkflowExecution>? executions;

    protected override async Task OnInitializedAsync() => await ReloadAsync();

    private async Task ReloadAsync()
    {
        var actor = await FlowSharp.Web.Security.CurrentUser.ResolveScopeAsync(AuthenticationStateProvider);
        executions = await ExecutionService.ListRecentAsync(actor);
    }

    private static string Duration(WorkflowExecution e) =>
        e.StartedAt is { } s && e.FinishedAt is { } f ? $"{(f - s).TotalSeconds:0.0} sn" : "—";
}
