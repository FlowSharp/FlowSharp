using System.Globalization;
using System.Text.Json.Nodes;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Data;

/// <summary>Item'lari bir alana gore siralar.</summary>
public sealed class SortNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        "sort.items", "Sort", NodeCategory.Data, NodeKind.Transform, "Item'lari bir alana gore siralar.",
        [
            new NodeParameterDefinition("field", "Field", NodeParameterType.String, IsRequired: true),
            new NodeParameterDefinition("order", "Order", NodeParameterType.Select, DefaultValue: "asc", Options: ["asc", "desc"])
        ],
        ["data"], "bar-chart", Color: "#0aa06e");

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var field = context.GetString("field") ?? "";
        var desc = (context.GetString("order") ?? "asc") == "desc";
        var sorted = context.Items.OrderBy(item => Key(item, field), Comparer<object>.Create(Compare)).ToList();
        if (desc) sorted.Reverse();
        return Task.FromResult(NodeExecutionResult.Single(sorted));
    }

    private static object Key(NodeItem item, string field) =>
        item.Json.TryGetPropertyValue(field, out var v) ? v?.ToString() ?? "" : "";

    private static int Compare(object? a, object? b)
    {
        var sa = a?.ToString() ?? ""; var sb = b?.ToString() ?? "";
        if (double.TryParse(sa, NumberStyles.Any, CultureInfo.InvariantCulture, out var na) &&
            double.TryParse(sb, NumberStyles.Any, CultureInfo.InvariantCulture, out var nb))
        {
            return na.CompareTo(nb);
        }
        return string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Item sayisini sinirlar.</summary>
public sealed class LimitNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        "limit.items", "Limit", NodeCategory.Data, NodeKind.Transform, "Maksimum item sayisini sinirlar.",
        [
            new NodeParameterDefinition("max", "Max Items", NodeParameterType.Number, DefaultValue: "10"),
            new NodeParameterDefinition("keep", "Keep", NodeParameterType.Select, DefaultValue: "first", Options: ["first", "last"])
        ],
        ["data"], "hash", Color: "#0aa06e");

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var max = Math.Max(0, context.GetInt("max", defaultValue: 10));
        var keepLast = (context.GetString("keep") ?? "first") == "last";
        var items = keepLast
            ? context.Items.TakeLast(max).ToList()
            : context.Items.Take(max).ToList();
        return Task.FromResult(NodeExecutionResult.Single(items));
    }
}

/// <summary>Tum item'lari tek bir item icinde bir diziye toplar.</summary>
public sealed class AggregateNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        "aggregate.items", "Aggregate", NodeCategory.Data, NodeKind.Transform, "Tum item'lari tek item'da toplar.",
        [new NodeParameterDefinition("destinationField", "Destination Field", NodeParameterType.String, DefaultValue: "data")],
        ["data"], "sigma", Color: "#0aa06e");

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var field = context.GetString("destinationField") ?? "data";
        var array = new JsonArray();
        foreach (var item in context.Items)
        {
            array.Add(item.Json.DeepClone());
        }
        return Task.FromResult(NodeExecutionResult.Single(NodeItem.From(new JsonObject { [field] = array })));
    }
}

/// <summary>Bir item'daki dizi alanini birden cok item'a boler.</summary>
public sealed class SplitOutNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        "split.out", "Split Out", NodeCategory.Data, NodeKind.Transform, "Bir dizi alanini ayri item'lara boler.",
        [new NodeParameterDefinition("field", "Field To Split Out", NodeParameterType.String, IsRequired: true)],
        ["data"], "scissors", Color: "#0aa06e");

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var field = context.GetString("field") ?? "";
        var output = new List<NodeItem>();

        foreach (var item in context.Items)
        {
            if (item.Json.TryGetPropertyValue(field, out var value) && value is JsonArray array)
            {
                foreach (var element in array)
                {
                    output.Add(element is JsonObject obj
                        ? NodeItem.From((JsonObject)obj.DeepClone())
                        : NodeItem.From(new JsonObject { ["value"] = element?.DeepClone() }));
                }
            }
            else
            {
                output.Add(item);
            }
        }

        return Task.FromResult(NodeExecutionResult.Single(output));
    }
}
