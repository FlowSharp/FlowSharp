using Bunit;
using FluentAssertions;
using FlowSharp.Application.Abstractions;
using FlowSharp.Application.Credentials;
using FlowSharp.Application.Nodes;
using FlowSharp.Application.Nodes.Expressions;
using FlowSharp.Application.Workflows;
using FlowSharp.Domain.Nodes;
using FlowSharp.Infrastructure.Data;
using FlowSharp.Infrastructure.Workflows.Expressions;
using FlowSharp.Tests.Fixtures;
using FlowSharp.Web.Components.Pages;
using FlowSharp.Web.Localization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Security.Claims;
using Xunit;

namespace FlowSharp.Tests.Web;

/// <summary>
/// WorkflowDesigner Blazor bilesenini bUnit ile render edip DOM davranisini dogrular.
/// JS interop bUnit tarafindan (Loose mode) otomatik karsilanir; servisler mock'lanir,
/// EF icin gercek in-memory SQLite kullanilir.
/// </summary>
public class WorkflowDesignerComponentTests : IDisposable
{
    private readonly SqliteDbFixture db = new();
    private readonly BunitContext ctx = new();

    public WorkflowDesignerComponentTests()
    {
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var localizer = Substitute.For<ILocalizer>();
        localizer[Arg.Any<string>()].Returns(call => (string)call[0]); // anahtari aynen don

        var catalog = Substitute.For<INodeCatalog>();
        catalog.GetAll().Returns(new List<NodeDefinition>());

        var credStore = Substitute.For<ICredentialStore>();
        credStore.ListAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new List<CredentialSummary>());

        var credCatalog = Substitute.For<ICredentialCatalog>();

        ctx.Services.AddSingleton(localizer);
        ctx.Services.AddSingleton(catalog);
        ctx.Services.AddSingleton(credStore);
        ctx.Services.AddSingleton(credCatalog);
        ctx.Services.AddSingleton(Substitute.For<IWorkflowQueue>());
        ctx.Services.AddSingleton(Substitute.For<IWorkflowExecutionEngine>());
        ctx.Services.AddSingleton(Substitute.For<IWebhookRegistrar>());
        ctx.Services.AddSingleton(Substitute.For<FlowSharp.Web.Notifications.IUiNotifier>());
        ctx.Services.AddSingleton(Substitute.For<IWorkflowExecutionTracker>());
        ctx.Services.AddSingleton(Substitute.For<IWorkflowEventPublisher>());
        ctx.Services.AddSingleton<IExpressionEvaluator>(new ExpressionEvaluator());
        ctx.Services.AddScoped(_ => db.NewContext());
        ctx.Services.AddSingleton<ApplicationDbContext>(db.NewContext());
        ctx.Services.AddSingleton<AuthenticationStateProvider>(new AnonymousAuthStateProvider());
    }

    /// <summary>Kimligi dogrulanmamis bir principal dondurur; sahiplik filtreleri icin yeterli.</summary>
    private sealed class AnonymousAuthStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    public void Dispose()
    {
        ctx.Dispose();
        db.Dispose();
    }

    [Fact]
    public void Empty_designer_renders_toolbar_with_default_name()
    {
        var cut = ctx.Render<WorkflowDesigner>();

        var nameInput = cut.Find("input.name-input");
        nameInput.GetAttribute("value").Should().Be("New workflow");
    }

    [Fact]
    public void Run_button_is_disabled_when_no_nodes()
    {
        var cut = ctx.Render<WorkflowDesigner>();

        var runButton = cut.FindAll("button").First(b => b.TextContent.Contains("common.run"));
        runButton.HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Editable_designer_shows_save_button_and_no_history_banner()
    {
        var cut = ctx.Render<WorkflowDesigner>();

        cut.FindAll("button").Should().Contain(b => b.TextContent.Contains("common.save"));
        cut.FindAll(".execution-banner").Should().BeEmpty();
    }

    [Fact]
    public void ReadOnly_designer_shows_history_banner_and_hides_run_save()
    {
        var cut = ctx.Render<WorkflowDesigner>(parameters => parameters
            .Add(p => p.ExecutionId, Guid.NewGuid()));

        cut.FindAll(".execution-banner").Should().ContainSingle();
        cut.FindAll("button").Should().NotContain(b => b.TextContent.Contains("common.save"));
    }

    [Fact]
    public void Designer_root_has_readonly_css_class_in_execution_mode()
    {
        var cut = ctx.Render<WorkflowDesigner>(parameters => parameters
            .Add(p => p.ExecutionId, Guid.NewGuid()));

        cut.Find(".nwf-designer").ClassList.Should().Contain("readonly-exec");
    }
}
