using System.Text.Json.Nodes;
using FluentAssertions;
using FlowSharp.Application.Nodes;
using FlowSharp.Nodes.Ai.Tools;
using FlowSharp.Nodes.Developer;
using FlowSharp.Tests.Fixtures;
using Xunit;

namespace FlowSharp.Tests.Nodes;

public class DeveloperAndAiNodeTests
{
    private static FakeNodeExecutionContext Ctx(JsonObject parameters, params JsonObject[] items) =>
        new(parameters, items.Length == 0 ? [NodeItem.Empty()] : items.Select(NodeItem.From).ToList());

    // ---- Code (Jint) ----
    [Fact]
    public async Task Code_maps_items_via_javascript()
    {
        var node = new CodeNode();
        var ctx = Ctx(new JsonObject
        {
            ["script"] = "return items.map(i => ({ json: { doubled: i.json.n * 2 } }));"
        },
            new JsonObject { ["n"] = 3 }, new JsonObject { ["n"] = 5 });

        var items = (await node.ExecuteAsync(ctx)).PrimaryItems;
        items.Should().HaveCount(2);
        items[0].Json["doubled"]!.GetValue<int>().Should().Be(6);
        items[1].Json["doubled"]!.GetValue<int>().Should().Be(10);
    }

    [Fact]
    public async Task Code_returns_failure_on_script_error()
    {
        var node = new CodeNode();
        var ctx = Ctx(new JsonObject { ["script"] = "return notDefined.boom();" }, new JsonObject());

        var result = await node.ExecuteAsync(ctx);
        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("Code hatasi");
    }

    [Fact]
    public async Task Code_default_passes_items_through()
    {
        var node = new CodeNode();
        var ctx = Ctx(new JsonObject { ["script"] = "return items;" },
            new JsonObject { ["a"] = 1 });

        var items = (await node.ExecuteAsync(ctx)).PrimaryItems;
        items.Single().Json["a"]!.GetValue<int>().Should().Be(1);
    }

    // ---- Calculator tool ----
    [Fact]
    public async Task Calculator_evaluates_expression()
    {
        var node = new CalculatorToolNode();
        var ctx = Ctx(new JsonObject(), new JsonObject { ["input"] = "2*(3+4)" });

        var item = (await node.ExecuteAsync(ctx)).PrimaryItems.Single().Json;
        item["result"]!.GetValue<string>().Should().Be("14");
    }

    [Fact]
    public async Task Calculator_returns_error_field_on_bad_expression()
    {
        var node = new CalculatorToolNode();
        var ctx = Ctx(new JsonObject(), new JsonObject { ["input"] = "2 +" });

        var item = (await node.ExecuteAsync(ctx)).PrimaryItems.Single().Json;
        item.ContainsKey("error").Should().BeTrue();
    }

    // ---- AI servis hata kurali (merkezi ceviriciye kayitli) ----
    [Fact]
    public void Ai_rate_limit_error_maps_to_actionable_message()
    {
        var error = FlowSharp.Nodes.Ai.AiErrorRules.HttpOperation.Describe(
            new Microsoft.SemanticKernel.HttpOperationException { StatusCode = System.Net.HttpStatusCode.TooManyRequests });

        error.Message.Should().Contain("429");
        error.Category.Should().Be(FlowSharp.Application.Errors.ErrorCategory.RateLimit);
    }

    [Fact]
    public void Ai_unauthorized_error_maps_to_auth_message()
    {
        var error = FlowSharp.Nodes.Ai.AiErrorRules.HttpOperation.Describe(
            new Microsoft.SemanticKernel.HttpOperationException { StatusCode = System.Net.HttpStatusCode.Unauthorized });

        error.Category.Should().Be(FlowSharp.Application.Errors.ErrorCategory.Authentication);
        error.Message.Should().Contain("API anahtari");
    }
}
