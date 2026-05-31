using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FlowSharp.Application.Abstractions;
using FlowSharp.Domain.Triggers;
using FlowSharp.Infrastructure.Data;

namespace FlowSharp.Infrastructure.Triggers;

public sealed class WebhookRegistrar(ApplicationDbContext dbContext) : IWebhookRegistrar
{
    public async Task SyncAsync(Guid workflowId, JsonElement definition, bool isActive, CancellationToken cancellationToken = default)
    {
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
            if (!node.TryGetProperty("type", out var typeEl) ||
                !string.Equals(typeEl.GetString(), "webhook.trigger", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = node.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "Webhook" : "Webhook";
            var parameters = node.TryGetProperty("parameters", out var paramEl) ? paramEl : default;
            var path = ReadParam(parameters, "path") ?? "my-webhook";
            var method = (ReadParam(parameters, "method") ?? "POST").ToUpperInvariant();

            dbContext.WebhookRegistrations.Add(new WebhookRegistration
            {
                WorkflowId = workflowId,
                NodeName = name,
                Method = method,
                Path = path.Trim('/'),
                IsActive = true
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<WebhookMatch?> ResolveAsync(string method, string path, CancellationToken cancellationToken = default)
    {
        var normalized = path.Trim('/');
        var registration = await dbContext.WebhookRegistrations
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.IsActive && item.Path == normalized && item.Method == method.ToUpperInvariant(), cancellationToken);

        return registration is null ? null : new WebhookMatch(registration.WorkflowId, registration.NodeName);
    }

    private static string? ReadParam(JsonElement parameters, string key) =>
        parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty(key, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText()
            : null;
}
