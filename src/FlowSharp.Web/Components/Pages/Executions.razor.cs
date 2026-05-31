using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlowSharp.Domain.Workflows;
using FlowSharp.Infrastructure.Data;

namespace FlowSharp.Web.Components.Pages;

public partial class Executions
{
    [Inject] public ApplicationDbContext DbContext { get; set; } = default!;

    private List<WorkflowExecution>? executions;

    protected override async Task OnInitializedAsync() => await ReloadAsync();

    private async Task ReloadAsync()
    {
        executions = await DbContext.WorkflowExecutions
            .Include(e => e.Workflow)
            .OrderByDescending(e => e.CreatedAt)
            .Take(50)
            .AsNoTracking()
            .ToListAsync();
    }

    private static string Duration(WorkflowExecution e) =>
        e.StartedAt is { } s && e.FinishedAt is { } f ? $"{(f - s).TotalSeconds:0.0} sn" : "—";
}
