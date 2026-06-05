using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FlowSharp.Application.Abstractions;
using FlowSharp.Domain.Triggers;
using FlowSharp.Infrastructure.Data;
using FlowSharp.Infrastructure.Identity;

namespace FlowSharp.Infrastructure.Triggers;

public sealed class WebhookRegistrar(ApplicationDbContext dbContext) : IWebhookRegistrar
{
    public async Task SyncAsync(Guid workflowId, JsonElement definition, bool isActive, CancellationToken cancellationToken = default)
    {
        var workflowKey = await ResolveWorkflowKeyAsync(workflowId, cancellationToken);

        // Once bu workflow'un mevcut kayitlarini temizle, sonra yeniden olustur.
        await dbContext.WebhookRegistrations
            .Where(registration => registration.WorkflowId == workflowId)
            .ExecuteDeleteAsync(cancellationToken);

        if (!isActive || !definition.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var node in nodes.EnumerateArray())
        {
            var type = node.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
            var parameters = node.TryGetProperty("parameters", out var paramEl) ? paramEl : default;
            var name = node.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "Webhook" : "Webhook";

            if (string.Equals(type, "webhook.trigger", StringComparison.OrdinalIgnoreCase))
            {
                var path = ReadParam(parameters, "path") ?? "my-webhook";
                var method = (ReadParam(parameters, "method") ?? "POST").ToUpperInvariant();
                AddRegistration(workflowId, workflowKey, name, method, path);
            }
            else if (string.Equals(type, "whatsapp.trigger", StringComparison.OrdinalIgnoreCase))
            {
                // WhatsApp tek bir URL'i hem GET (Meta dogrulama) hem POST (olaylar) icin kullanir.
                var path = ReadParam(parameters, "path") ?? "whatsapp";
                AddRegistration(workflowId, workflowKey, name, "GET", path);
                AddRegistration(workflowId, workflowKey, name, "POST", path);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<WebhookMatch?> ResolveAsync(string? workflowKey, string method, string path, CancellationToken cancellationToken = default)
    {
        var normalized = path.Trim('/');
        var upperMethod = method.ToUpperInvariant();
        var registration = await dbContext.WebhookRegistrations
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.IsActive
                && item.WorkflowKey == workflowKey
                && item.Path == normalized
                && item.Method == upperMethod, cancellationToken);

        if (registration is null)
        {
            return null;
        }

        // whatsapp.trigger icin "event" filtresini (messages|statuses|all) tanimdan oku; boylece
        // endpoint status-only webhook'larda workflow'u (ve AI'i) bos yere tetiklemez.
        var eventFilter = await ResolveWhatsAppEventFilterAsync(registration.WorkflowId, registration.NodeName, cancellationToken);
        return new WebhookMatch(registration.WorkflowId, registration.NodeName, eventFilter);
    }

    private async Task<string?> ResolveWhatsAppEventFilterAsync(Guid workflowId, string nodeName, CancellationToken cancellationToken)
    {
        var definition = await dbContext.Workflows
            .AsNoTracking()
            .Where(workflow => workflow.Id == workflowId)
            .Select(workflow => workflow.Definition)
            .FirstOrDefaultAsync(cancellationToken);

        if (definition is null || !definition.RootElement.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var node in nodes.EnumerateArray())
        {
            if (node.TryGetProperty("type", out var typeEl)
                && string.Equals(typeEl.GetString(), "whatsapp.trigger", StringComparison.OrdinalIgnoreCase)
                && node.TryGetProperty("name", out var nameEl)
                && string.Equals(nameEl.GetString(), nodeName, StringComparison.Ordinal))
            {
                var parameters = node.TryGetProperty("parameters", out var paramEl) ? paramEl : default;
                return ReadParam(parameters, "event");
            }
        }

        return null;
    }

    private void AddRegistration(Guid workflowId, string workflowKey, string nodeName, string method, string path) =>
        dbContext.WebhookRegistrations.Add(new WebhookRegistration
        {
            WorkflowId = workflowId,
            WorkflowKey = workflowKey,
            NodeName = nodeName,
            Method = method,
            Path = path.Trim('/'),
            IsActive = true
        });

    private static string? ReadParam(JsonElement parameters, string key) =>
        parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty(key, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText()
            : null;

    private async Task<string> ResolveWorkflowKeyAsync(Guid workflowId, CancellationToken cancellationToken)
    {
        var existingKey = await dbContext.WebhookRegistrations
            .AsNoTracking()
            .Where(registration => registration.WorkflowId == workflowId && !string.IsNullOrEmpty(registration.WorkflowKey))
            .Select(registration => registration.WorkflowKey)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(existingKey) &&
            !await dbContext.WebhookRegistrations.AsNoTracking()
                .AnyAsync(registration => registration.WorkflowId != workflowId && registration.WorkflowKey == existingKey, cancellationToken))
        {
            return existingKey;
        }

        string candidate;
        do
        {
            candidate = WebhookKeyGenerator.Generate();
        }
        while (await dbContext.WebhookRegistrations.AsNoTracking()
            .AnyAsync(registration => registration.WorkflowKey == candidate, cancellationToken));

        return candidate;
    }
}
