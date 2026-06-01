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

/// <summary>
/// Item'lari toplar: tek diziye toplama (collect) veya sayisal bir alanda
/// count/sum/avg/min/max. "Group By" verilirse her grup ayri bir item olur.
/// </summary>
public sealed class AggregateNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        "aggregate.items", "Aggregate", NodeCategory.Data, NodeKind.Transform,
        "Item'lari toplar; collect/count/sum/avg/min/max ve opsiyonel Group By.",
        [
            new NodeParameterDefinition("operation", "Operation", NodeParameterType.Select, DefaultValue: "collect",
                Options: ["collect", "count", "sum", "avg", "min", "max"]),
            new NodeParameterDefinition("groupBy", "Group By", NodeParameterType.String,
                HelpText: "Opsiyonel. Bu alana gore gruplar; her grup bir item olur."),
            new NodeParameterDefinition("field", "Field", NodeParameterType.String,
                HelpText: "sum/avg/min/max icin sayisal alan adi.",
                ShowWhen: new ParameterCondition("operation", ["sum", "avg", "min", "max"])),
            new NodeParameterDefinition("destinationField", "Destination Field", NodeParameterType.String, DefaultValue: "data",
                HelpText: "collect islemi icin dizi alani adi.",
                ShowWhen: new ParameterCondition("operation", ["collect"]))
        ],
        ["data"], "sigma", Color: "#0aa06e");

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var operation = (context.GetString("operation") ?? "collect").ToLowerInvariant();
        var groupBy = context.GetString("groupBy");
        var field = context.GetString("field");
        var destinationField = context.GetString("destinationField") ?? "data";

        if (string.IsNullOrWhiteSpace(groupBy))
        {
            var single = new JsonObject();
            Fill(single, context.Items, operation, field, destinationField);
            return Task.FromResult(NodeExecutionResult.Single(NodeItem.From(single)));
        }

        var outputs = new List<NodeItem>();
        foreach (var group in context.Items.GroupBy(item => item.Json[groupBy]?.ToString() ?? "null"))
        {
            var members = group.ToList();
            var obj = new JsonObject { [groupBy] = members[0].Json[groupBy]?.DeepClone() };
            Fill(obj, members, operation, field, destinationField);
            outputs.Add(NodeItem.From(obj));
        }

        return Task.FromResult(NodeExecutionResult.Single(outputs));
    }

    private static void Fill(JsonObject obj, IReadOnlyList<NodeItem> items, string operation, string? field, string destinationField)
    {
        obj["count"] = items.Count;

        if (operation == "collect")
        {
            var array = new JsonArray();
            foreach (var item in items)
            {
                array.Add(item.Json.DeepClone());
            }
            obj[destinationField] = array;
            return;
        }

        if (operation == "count")
        {
            return; // count zaten yazildi
        }

        var values = items
            .Select(item => string.IsNullOrWhiteSpace(field) ? null : TryGetNumber(item.Json[field]))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();

        double result = values.Count == 0 ? 0 : operation switch
        {
            "avg" => values.Average(),
            "min" => values.Min(),
            "max" => values.Max(),
            _ => values.Sum()
        };

        obj["field"] = field;
        obj[operation] = result;
    }

    private static double? TryGetNumber(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<double>(out var d)) return d;
            if (value.TryGetValue<string>(out var s) &&
                double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        }
        return null;
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
