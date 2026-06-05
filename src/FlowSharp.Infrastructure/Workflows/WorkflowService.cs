using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FlowSharp.Application.Abstractions;
using FlowSharp.Application.Security;
using FlowSharp.Application.Workflows;
using FlowSharp.Domain.Workflows;
using FlowSharp.Infrastructure.Data;

namespace FlowSharp.Infrastructure.Workflows;

/// <inheritdoc cref="IWorkflowService"/>
public sealed class WorkflowService(
    ApplicationDbContext dbContext,
    IWorkflowRunner runner,
    IWebhookRegistrar webhookRegistrar) : IWorkflowService
{
    // Silinen workflow'un webhook kayitlarini temizlemek icin bos tanim.
    private static readonly JsonDocument EmptyDefinition = JsonDocument.Parse("""{"nodes":[]}""");

    public async Task<IReadOnlyList<Workflow>> ListAsync(
        ActorScope actor, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Workflows.AsNoTracking().AsQueryable();
        if (!actor.IsAdmin)
        {
            query = query.Where(workflow => workflow.OwnerId == actor.UserId);
        }

        return await query
            .OrderByDescending(workflow => workflow.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Workflow?> GetForEditAsync(
        Guid id, ActorScope actor, CancellationToken cancellationToken = default)
    {
        var workflow = await dbContext.Workflows.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (workflow is null || (!actor.IsAdmin && workflow.OwnerId != actor.UserId))
        {
            return null;
        }

        return workflow;
    }

    public async Task<bool> OwnsAsync(Guid id, ActorScope actor, CancellationToken cancellationToken = default) =>
        actor.IsAdmin ||
        await dbContext.Workflows.AnyAsync(
            workflow => workflow.Id == id && workflow.OwnerId == actor.UserId, cancellationToken);

    public async Task<WorkflowRunResult> RunAsync(
        Guid id, JsonDocument payload, ActorScope actor, CancellationToken cancellationToken = default)
    {
        if (!await OwnsAsync(id, actor, cancellationToken))
        {
            throw new UnauthorizedAccessException("Bu workflow'u calistirma yetkiniz yok.");
        }

        return await runner.ExecuteNowAsync(id, payload, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, ActorScope actor, CancellationToken cancellationToken = default)
    {
        if (!await OwnsAsync(id, actor, cancellationToken))
        {
            throw new UnauthorizedAccessException("Bu workflow'u silme yetkiniz yok.");
        }

        await webhookRegistrar.SyncAsync(id, EmptyDefinition.RootElement, false, cancellationToken);
        await dbContext.Workflows
            .Where(workflow => workflow.Id == id && (actor.IsAdmin || workflow.OwnerId == actor.UserId))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<WorkflowSaveResult> SaveAsync(
        WorkflowSaveInput input, ActorScope actor, CancellationToken cancellationToken = default)
    {
        Workflow workflow;
        if (input.Id is { } id)
        {
            workflow = await dbContext.Workflows.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Guncellenecek workflow bulunamadi.");

            // Sahiplik: baskasinin workflow'unu guncellemeyi engelle (sahip degismez).
            if (!actor.IsAdmin && workflow.OwnerId != actor.UserId)
            {
                throw new UnauthorizedAccessException("Bu workflow'u duzenleme yetkiniz yok.");
            }

            workflow.Name = input.Name;
            workflow.Description = input.Description;
            workflow.IsActive = input.IsActive;
            workflow.Definition = input.Definition;
            workflow.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            workflow = new Workflow
            {
                Name = input.Name,
                Description = input.Description,
                IsActive = input.IsActive,
                Definition = input.Definition,
                OwnerId = actor.UserId
            };
            dbContext.Workflows.Add(workflow);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await webhookRegistrar.SyncAsync(workflow.Id, workflow.Definition.RootElement, input.IsActive, cancellationToken);
        var webhookKey = await GetWebhookKeyAsync(workflow.Id, cancellationToken);
        return new WorkflowSaveResult(workflow.Id, webhookKey);
    }

    public async Task<string?> GetWebhookKeyAsync(Guid workflowId, CancellationToken cancellationToken = default) =>
        await dbContext.WebhookRegistrations
            .AsNoTracking()
            .Where(registration => registration.WorkflowId == workflowId)
            .Select(registration => registration.WorkflowKey)
            .FirstOrDefaultAsync(cancellationToken);
}
