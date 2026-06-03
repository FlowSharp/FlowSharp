using System.Text.Json.Nodes;
using FluentAssertions;
using FlowSharp.Application.Nodes;
using FlowSharp.Application.Nodes.Expressions;
using FlowSharp.Infrastructure.Workflows.Expressions;
using Xunit;

namespace FlowSharp.Tests.Expressions;

public class ExpressionEvaluatorTests
{
    private readonly ExpressionEvaluator evaluator = new();

    private static ExpressionContext Context(JsonObject? json = null, JsonObject? trigger = null,
        IReadOnlyDictionary<string, IReadOnlyList<NodeItem>>? outputs = null, int itemIndex = 0, int runIndex = 0)
        => new()
        {
            CurrentItem = json is null ? null : NodeItem.From(json),
            ItemIndex = itemIndex,
            RunIndex = runIndex,
            Trigger = trigger,
            NodeOutputs = outputs ?? new Dictionary<string, IReadOnlyList<NodeItem>>(StringComparer.OrdinalIgnoreCase)
        };

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("duz metin", false)]
    [InlineData("{{ $json.x }}", true)]
    [InlineData("yarim {{", false)]
    public void ContainsExpression_detects_braces(string? template, bool expected) =>
        evaluator.ContainsExpression(template).Should().Be(expected);

    [Fact]
    public void EvaluateToString_returns_literal_when_no_expression() =>
        evaluator.EvaluateToString("merhaba", Context()).Should().Be("merhaba");

    [Fact]
    public void EvaluateToString_resolves_json_path()
    {
        var ctx = Context(new JsonObject { ["name"] = "Eray" });
        evaluator.EvaluateToString("Selam {{ $json.name }}!", ctx).Should().Be("Selam Eray!");
    }

    [Fact]
    public void EvaluateToString_resolves_nested_and_array_access()
    {
        var ctx = Context(new JsonObject
        {
            ["user"] = new JsonObject { ["roles"] = new JsonArray("admin", "editor") }
        });
        evaluator.EvaluateToString("{{ $json.user.roles[1] }}", ctx).Should().Be("editor");
    }

    [Fact]
    public void EvaluateToString_supports_bracket_quoted_keys()
    {
        var ctx = Context(new JsonObject { ["first name"] = "Ada" });
        evaluator.EvaluateToString("{{ $json[\"first name\"] }}", ctx).Should().Be("Ada");
    }

    [Fact]
    public void EvaluateToNode_preserves_number_type_for_single_expression()
    {
        var ctx = Context(new JsonObject { ["age"] = 42 });
        var node = evaluator.EvaluateToNode("{{ $json.age }}", ctx);
        node!.GetValue<int>().Should().Be(42);
    }

    [Fact]
    public void EvaluateToNode_preserves_object_type_for_single_expression()
    {
        var ctx = Context(new JsonObject { ["data"] = new JsonObject { ["k"] = "v" } });
        var node = evaluator.EvaluateToNode("{{ $json.data }}", ctx);
        node.Should().BeOfType<JsonObject>();
        node!["k"]!.GetValue<string>().Should().Be("v");
    }

    [Fact]
    public void EvaluateToNode_stringifies_when_mixed_with_text()
    {
        var ctx = Context(new JsonObject { ["age"] = 42 });
        var node = evaluator.EvaluateToNode("yas: {{ $json.age }}", ctx);
        node!.GetValue<string>().Should().Be("yas: 42");
    }

    [Fact]
    public void Evaluate_resolves_trigger_reference()
    {
        var ctx = Context(trigger: new JsonObject { ["source"] = "webhook" });
        evaluator.EvaluateToString("{{ $trigger.source }}", ctx).Should().Be("webhook");
    }

    [Fact]
    public void Evaluate_resolves_node_output_reference()
    {
        var outputs = new Dictionary<string, IReadOnlyList<NodeItem>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Önceki"] = [NodeItem.From(new JsonObject { ["total"] = 7 })]
        };
        var ctx = Context(outputs: outputs);
        evaluator.EvaluateToString("{{ $node[\"Önceki\"].json.total }}", ctx).Should().Be("7");
    }

    [Fact]
    public void Evaluate_itemIndex_and_runIndex()
    {
        var ctx = Context(itemIndex: 3, runIndex: 2);
        evaluator.EvaluateToString("{{ $itemIndex }}-{{ $runIndex }}", ctx).Should().Be("3-2");
    }

    [Fact]
    public void Evaluate_unknown_reference_throws()
    {
        var ctx = Context(new JsonObject { ["x"] = 1 });
        var act = () => evaluator.EvaluateToString("{{ $json.missing }}", ctx);

        act.Should().Throw<ExpressionEvaluationException>()
            .WithMessage("Ifade cozulemedi*");
    }

    [Fact]
    public void Evaluate_unparseable_root_throws()
    {
        var ctx = Context(new JsonObject());
        var act = () => evaluator.EvaluateToString("a{{ $foo.bar }}b", ctx);

        act.Should().Throw<ExpressionEvaluationException>()
            .WithMessage("Ifade cozulemedi*");
    }

    [Fact]
    public void Evaluate_now_returns_iso_timestamp()
    {
        var value = evaluator.EvaluateToString("{{ $now }}", Context());
        DateTimeOffset.TryParse(value, out _).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_today_returns_date()
    {
        evaluator.EvaluateToString("{{ $today }}", Context())
            .Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$");
    }

    [Fact]
    public void Evaluate_item_wraps_json_under_json_key()
    {
        var ctx = Context(new JsonObject { ["x"] = 1 });
        evaluator.EvaluateToString("{{ $item.json.x }}", ctx).Should().Be("1");
    }

    [Fact]
    public void Evaluate_node_without_name_throws()
    {
        var act = () => evaluator.EvaluateToString("{{ $node }}", Context());

        act.Should().Throw<ExpressionEvaluationException>()
            .WithMessage("Ifade cozulemedi*");
    }

    [Fact]
    public void Evaluate_array_index_out_of_range_throws()
    {
        var ctx = Context(new JsonObject { ["arr"] = new JsonArray(1, 2) });
        var act = () => evaluator.EvaluateToString("{{ $json.arr[5] }}", ctx);

        act.Should().Throw<ExpressionEvaluationException>()
            .WithMessage("Ifade cozulemedi*");
    }

    [Fact]
    public void Evaluate_object_value_is_serialized_in_string_context()
    {
        var ctx = Context(new JsonObject { ["o"] = new JsonObject { ["k"] = "v" } });
        evaluator.EvaluateToString("{{ $json.o }}", ctx).Should().Contain("\"k\"");
    }

    [Fact]
    public void EvaluateToNode_returns_null_for_null_template() =>
        evaluator.EvaluateToNode(null, Context()).Should().BeNull();
}
