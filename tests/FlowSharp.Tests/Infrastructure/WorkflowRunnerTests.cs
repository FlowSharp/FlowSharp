using System.Text.Json;
using FluentAssertions;
using FlowSharp.Application.Abstractions;
using FlowSharp.Application.Workflows;
using FlowSharp.Domain.Queue;
using FlowSharp.Domain.Workflows;
using FlowSharp.Infrastructure.Queue;
using FlowSharp.Infrastructure.Workflows;
using FlowSharp.Tests.Fixtures;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace FlowSharp.Tests.Infrastructure;

public class WorkflowRunnerTests : IDisposable
{
    private readonly SqliteDbFixture db = new();
    private readonly IWorkflowEventPublisher events = Substitute.For<IWorkflowEventPublisher>();

    public void Dispose() => db.Dispose();

    private WorkflowRunner NewRunner(out SqliteWorkflowQueue queue)
    {
        var ctx = db.NewContext();
        queue = new SqliteWorkflowQueue(ctx);
        var engine = EngineHarness.Create();
        return new WorkflowRunner(
            ctx, engine, events, queue,
            Options.Create(new ExecutionOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkflowRunner>.Instance);
    }

    private async Task<Workflow> AddWorkflow(string definitionJson, bool active = true)
    {
        await using var ctx = db.NewContext();
        var wf = new Workflow
        {
            Name = "Test",
            IsActive = active,
            Definition = JsonDocument.Parse(definitionJson)
        };
        ctx.Workflows.Add(wf);
        await ctx.SaveChangesAsync();
        return wf;
    }

    [Fact]
    public async Task ExecuteNow_runs_workflow_and_persists_succeeded_execution()
    {
        var def = new WorkflowBuilder()
            .Node("t", "test.trigger", "Trigger")
            .Node("a", "test.tag", "A", new System.Text.Json.Nodes.JsonObject { ["tag"] = "a" })
            .Connect("t", "a")
            .Build().RootElement.GetRawText();
        var wf = await AddWorkflow(def);

        var runner = NewRunner(out _);
        var result = await runner.ExecuteNowAsync(wf.Id, JsonDocument.Parse("""{"source":"manual"}"""));

        result.Succeeded.Should().BeTrue();

        await using var ctx = db.NewContext();
        var execution = ctx.WorkflowExecutions.Single();
        execution.Status.Should().Be(WorkflowExecutionStatus.Succeeded);
        execution.WorkflowId.Should().Be(wf.Id);
    }

    [Fact]
    public async Task Failing_workflow_marks_execution_failed_and_enqueues_error_workflow()
    {
        // Basarisiz olacak ana workflow.
        var failingDef = new WorkflowBuilder()
            .Node("t", "test.trigger", "Trigger")
            .Node("f", "test.fail", "Boom")
            .Connect("t", "f")
            .Build().RootElement.GetRawText();
        var failing = await AddWorkflow(failingDef);

        // error.trigger iceren aktif bir hata-yakalama workflow'u.
        var errorDef = new WorkflowBuilder()
            .Node("e", "error.trigger", "On Error")
            .Build().RootElement.GetRawText();
        await AddWorkflow(errorDef);

        var runner = NewRunner(out _);
        async Task<WorkflowRunResult> act() => await runner.ExecuteNowAsync(failing.Id, JsonDocument.Parse("""{"source":"manual"}"""));

        // ExecuteNow basarisizlikta exception atmaz; sonuc dondurur (RunAsync atar).
        var result = await act();
        result.Succeeded.Should().BeFalse();

        await using var ctx = db.NewContext();
        ctx.WorkflowExecutions.Single().Status.Should().Be(WorkflowExecutionStatus.Failed);
        // Hata workflow'u kuyruga eklenmis olmali.
        ctx.WorkflowJobs.Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_throws_when_workflow_fails()
    {
        var failingDef = new WorkflowBuilder()
            .Node("t", "test.trigger", "Trigger")
            .Node("f", "test.fail", "Boom")
            .Connect("t", "f")
            .Build().RootElement.GetRawText();
        var failing = await AddWorkflow(failingDef);

        var runner = NewRunner(out _);
        var job = new WorkflowJob { WorkflowId = failing.Id, Payload = JsonDocument.Parse("""{"source":"manual"}""") };

        var act = async () => await runner.RunAsync(job);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
