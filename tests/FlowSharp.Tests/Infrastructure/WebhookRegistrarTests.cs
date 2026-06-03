using System.Text.Json;
using FluentAssertions;
using FlowSharp.Infrastructure.Triggers;
using FlowSharp.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowSharp.Tests.Infrastructure;

public class WebhookRegistrarTests : IDisposable
{
    private readonly SqliteDbFixture db = new();

    public void Dispose() => db.Dispose();

    private WebhookRegistrar NewRegistrar() => new(db.NewContext());

    private static JsonElement Def(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Sync_registers_webhook_trigger_nodes()
    {
        var workflowId = Guid.NewGuid();
        var def = Def("""
        {"nodes":[
          {"type":"webhook.trigger","name":"In","parameters":{"path":"/orders/","method":"post"}}
        ]}
        """);

        await NewRegistrar().SyncAsync(workflowId, def, isActive: true);

        var key = await KeyForWorkflowAsync(workflowId);
        var match = await NewRegistrar().ResolveAsync(key, "POST", "orders");
        match.Should().NotBeNull();
        match!.WorkflowId.Should().Be(workflowId);
        match.NodeName.Should().Be("In");
    }

    [Fact]
    public async Task Sync_inactive_workflow_registers_nothing()
    {
        var def = Def("""{"nodes":[{"type":"webhook.trigger","parameters":{"path":"x"}}]}""");
        await NewRegistrar().SyncAsync(Guid.NewGuid(), def, isActive: false);

        (await NewRegistrar().ResolveAsync(null, "POST", "x")).Should().BeNull();
    }

    [Fact]
    public async Task Sync_replaces_previous_registrations_for_same_workflow()
    {
        var id = Guid.NewGuid();
        await NewRegistrar().SyncAsync(id, Def("""{"nodes":[{"type":"webhook.trigger","parameters":{"path":"old"}}]}"""), true);
        await NewRegistrar().SyncAsync(id, Def("""{"nodes":[{"type":"webhook.trigger","parameters":{"path":"new"}}]}"""), true);
        var key = await KeyForWorkflowAsync(id);

        (await NewRegistrar().ResolveAsync(key, "POST", "old")).Should().BeNull();
        (await NewRegistrar().ResolveAsync(key, "POST", "new")).Should().NotBeNull();
    }

    [Fact]
    public async Task Sync_defaults_path_and_method_when_missing()
    {
        var id = Guid.NewGuid();
        await NewRegistrar().SyncAsync(id,
            Def("""{"nodes":[{"type":"webhook.trigger","parameters":{}}]}"""), true);
        var key = await KeyForWorkflowAsync(id);

        // Varsayilanlar: path "my-webhook", method "POST".
        (await NewRegistrar().ResolveAsync(key, "POST", "my-webhook")).Should().NotBeNull();
    }

    [Fact]
    public async Task Resolve_is_case_insensitive_on_method()
    {
        var id = Guid.NewGuid();
        await NewRegistrar().SyncAsync(id,
            Def("""{"nodes":[{"type":"webhook.trigger","parameters":{"path":"hook","method":"GET"}}]}"""), true);
        var key = await KeyForWorkflowAsync(id);

        (await NewRegistrar().ResolveAsync(key, "get", "/hook/")).Should().NotBeNull();
    }

    [Fact]
    public async Task Resolve_unknown_returns_null()
    {
        (await NewRegistrar().ResolveAsync(null, "POST", "nope")).Should().BeNull();
    }

    [Fact]
    public async Task Sync_ignores_non_webhook_nodes()
    {
        await NewRegistrar().SyncAsync(Guid.NewGuid(),
            Def("""{"nodes":[{"type":"http.request","parameters":{"url":"x"}}]}"""), true);

        (await NewRegistrar().ResolveAsync(null, "POST", "x")).Should().BeNull();
    }

    [Fact]
    public async Task Same_path_for_different_workflows_is_isolated_by_workflow_key()
    {
        var wfA = Guid.NewGuid();
        var wfB = Guid.NewGuid();

        var def = Def("""{"nodes":[{"type":"webhook.trigger","name":"Hook","parameters":{"path":"shared"}}]}""");
        await NewRegistrar().SyncAsync(wfA, def, true);
        await NewRegistrar().SyncAsync(wfB, def, true);

        var keyA = await KeyForWorkflowAsync(wfA);
        var keyB = await KeyForWorkflowAsync(wfB);
        keyA.Should().NotBe(keyB);
        (await NewRegistrar().ResolveAsync(keyA, "POST", "shared"))!.WorkflowId.Should().Be(wfA);
        (await NewRegistrar().ResolveAsync(keyB, "POST", "shared"))!.WorkflowId.Should().Be(wfB);
        // A'nin anahtari asla B'nin workflow'unu tetikleyemez.
        (await NewRegistrar().ResolveAsync(keyA, "POST", "shared"))!.WorkflowId.Should().NotBe(wfB);
        // Bilinmeyen anahtar hicbir sey dondurmez.
        (await NewRegistrar().ResolveAsync("ZZZZZZZZZZZZZZZZ", "POST", "shared")).Should().BeNull();
    }

    private async Task<string> KeyForWorkflowAsync(Guid workflowId)
    {
        await using var ctx = db.NewContext();
        return await ctx.WebhookRegistrations
            .Where(registration => registration.WorkflowId == workflowId)
            .Select(registration => registration.WorkflowKey!)
            .FirstAsync();
    }
}
