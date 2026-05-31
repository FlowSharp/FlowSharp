using System.Globalization;
using System.Text.Json.Nodes;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Data;

/// <summary>Tarih/saat uretir veya bicimlendirir; sonucu bir alana yazar.</summary>
public sealed class DateTimeNode : PerItemNodeType
{
    public override NodeDefinition Definition { get; } = new(
        "datetime.action", "Date & Time", NodeCategory.Data, NodeKind.Transform, "Tarih/saat uretir veya bicimlendirir.",
        [
            new NodeParameterDefinition("operation", "Operation", NodeParameterType.Select, DefaultValue: "now",
                Options: ["now", "format"]),
            new NodeParameterDefinition("value", "Value", NodeParameterType.String,
                HelpText: "format icin giris tarihi. Ornek: {{$json.tarih}}"),
            new NodeParameterDefinition("format", "Format", NodeParameterType.String, DefaultValue: "yyyy-MM-dd HH:mm:ss"),
            new NodeParameterDefinition("outputField", "Output Field", NodeParameterType.String, DefaultValue: "datetime")
        ],
        ["data"], "clock", Color: "#0aa06e");

    protected override Task<NodeItem?> ProcessItemAsync(INodeExecutionContext context, NodeItem item, int index)
    {
        var operation = context.GetString("operation", index) ?? "now";
        var format = context.GetString("format", index) ?? "yyyy-MM-dd HH:mm:ss";
        var outputField = context.GetString("outputField", index) ?? "datetime";

        string result;
        if (operation == "format")
        {
            var raw = context.GetString("value", index);
            result = DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed.ToString(format, CultureInfo.InvariantCulture)
                : "";
        }
        else
        {
            result = DateTimeOffset.UtcNow.ToString(format, CultureInfo.InvariantCulture);
        }

        var json = (JsonObject)item.Json.DeepClone();
        json[outputField] = result;
        return Task.FromResult<NodeItem?>(NodeItem.From(json));
    }
}
