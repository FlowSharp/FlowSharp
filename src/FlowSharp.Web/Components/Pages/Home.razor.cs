using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlowSharp.Application.Workflows;
using FlowSharp.Domain.Workflows;

namespace FlowSharp.Web.Components.Pages;

public partial class Home
{
    [Inject] public IDashboardService DashboardService { get; set; } = default!;
    [Inject] public AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    private int workflowCount, activeCount, executionCount, failedCount;
    private IReadOnlyList<WorkflowExecution> recent = [];

    protected override async Task OnInitializedAsync()
    {
        var actor = await FlowSharp.Web.Security.CurrentUser.ResolveScopeAsync(AuthenticationStateProvider);
        var summary = await DashboardService.GetSummaryAsync(actor);

        workflowCount = summary.WorkflowCount;
        activeCount = summary.ActiveCount;
        executionCount = summary.ExecutionCount;
        failedCount = summary.FailedCount;
        recent = summary.RecentExecutions;
    }

    private static string Duration(WorkflowExecution e) =>
        e.StartedAt is { } s && e.FinishedAt is { } f ? $"{(f - s).TotalSeconds:0.0} sn" : "—";
}
