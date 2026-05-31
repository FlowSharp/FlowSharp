using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CsvHelper;
using CsvHelper.Configuration;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Data;

/// <summary>
/// CSV donusturme (CsvHelper): item'lari CSV metnine cevirir (toCsv) ya da bir CSV metnini
/// satir basina bir item olacak sekilde ayristirir (fromCsv).
/// </summary>
public sealed class CsvNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "transform.csv",
        DisplayName: "CSV",
        Category: NodeCategory.Data,
        Kind: NodeKind.Transform,
        Description: "Item'lari CSV'ye cevirir veya CSV'yi item'lara ayristirir.",
        Parameters:
        [
            new NodeParameterDefinition("operation", "Islem", NodeParameterType.Select, IsRequired: true,
                DefaultValue: "toCsv", Options: ["toCsv", "fromCsv"]),
            new NodeParameterDefinition("csvData", "CSV Verisi", NodeParameterType.Text,
                HelpText: "fromCsv icin: ayristirilacak CSV metni. Ornek: {{$json.body}}"),
            new NodeParameterDefinition("delimiter", "Ayirici", NodeParameterType.String, DefaultValue: ",")
        ],
        Tags: ["data", "csv"],
        Icon: "sliders",
        Color: "#27ae60");

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var operation = context.GetString("operation") ?? "toCsv";
        var delimiter = context.GetString("delimiter") is { Length: > 0 } d ? d : ",";
        var config = new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = delimiter };

        return Task.FromResult(operation == "fromCsv"
            ? FromCsv(context.GetString("csvData") ?? "", config)
            : ToCsv(context.Items, config));
    }

    private static NodeExecutionResult ToCsv(IReadOnlyList<NodeItem> items, CsvConfiguration config)
    {
        // Tum item'larin alan adlarini (ilk gorulme sirasiyla) birlestir.
        var headers = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            foreach (var pair in item.Json)
            {
                if (seen.Add(pair.Key))
                {
                    headers.Add(pair.Key);
                }
            }
        }

        using var writer = new StringWriter();
        using (var csv = new CsvWriter(writer, config))
        {
            foreach (var header in headers)
            {
                csv.WriteField(header);
            }

            csv.NextRecord();

            foreach (var item in items)
            {
                foreach (var header in headers)
                {
                    var value = item.Json.TryGetPropertyValue(header, out var node) ? node : null;
                    csv.WriteField(value is JsonValue ? value.ToString() : value?.ToJsonString() ?? "");
                }

                csv.NextRecord();
            }
        }

        return NodeExecutionResult.Single(NodeItem.From(new JsonObject { ["csv"] = writer.ToString() }));
    }

    private static NodeExecutionResult FromCsv(string data, CsvConfiguration config)
    {
        var items = new List<NodeItem>();
        if (string.IsNullOrWhiteSpace(data))
        {
            return NodeExecutionResult.Single(items);
        }

        using var reader = new StringReader(data);
        using var csv = new CsvReader(reader, config);
        csv.Read();
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? [];

        while (csv.Read())
        {
            var row = new JsonObject();
            foreach (var header in headers)
            {
                row[header] = csv.GetField(header);
            }

            items.Add(NodeItem.From(row));
        }

        return NodeExecutionResult.Single(items);
    }
}
