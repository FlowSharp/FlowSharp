using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlowSharp.Domain.Workflows;
using FlowSharp.Infrastructure.Data;

namespace FlowSharp.Web.Components.Pages;

public partial class Home
{
    [Inject] public required ApplicationDbContext DbContext { get; set; }

    private int workflowCount, activeCount, executionCount, failedCount;
    private List<WorkflowExecution> recent = [];

    protected override async Task OnInitializedAsync()
    {
        workflowCount = await DbContext.Workflows.CountAsync();
        activeCount = await DbContext.Workflows.CountAsync(w => w.IsActive);
        executionCount = await DbContext.WorkflowExecutions.CountAsync();
        failedCount = await DbContext.WorkflowExecutions.CountAsync(e => e.Status == WorkflowExecutionStatus.Failed);
        recent = await DbContext.WorkflowExecutions
            .Include(e => e.Workflow)
            .OrderByDescending(e => e.CreatedAt)
            .Take(8)
            .AsNoTracking()
            .ToListAsync();
    }

    private static string Duration(WorkflowExecution e) =>
        e.StartedAt is { } s && e.FinishedAt is { } f ? $"{(f - s).TotalSeconds:0.0} sn" : "—";
}
