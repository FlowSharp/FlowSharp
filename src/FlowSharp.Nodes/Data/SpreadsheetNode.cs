using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using FlowSharp.Application.Errors;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Data;

/// <summary>
/// Yuklenen bir Excel (.xlsx) veya CSV dosyasini okur; her satiri bir item'a cevirir.
/// "file" parametresi designer'dan yuklenen { fileName, content(base64) } yapisidir.
/// Ornek: urun listesi Excel'i yukle -> her urun bir item -> AI Agent'a ver.
/// </summary>
public sealed class SpreadsheetNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "transform.spreadsheet",
        DisplayName: "Spreadsheet",
        Category: NodeCategory.Data,
        Kind: NodeKind.Transform,
        Description: "Excel/CSV dosyasini okur; her satir bir item olur.",
        Parameters:
        [
            new NodeParameterDefinition("file", "Dosya (.xlsx/.csv)", NodeParameterType.File, IsRequired: true,
                HelpText: "Bilgisayardan Excel veya CSV dosyasi yukle."),
            new NodeParameterDefinition("sheet", "Sayfa adi", NodeParameterType.String,
                HelpText: "Excel icin sayfa adi (bos ise ilk sayfa)."),
            new NodeParameterDefinition("hasHeader", "Ilk satir baslik mi?", NodeParameterType.Boolean, DefaultValue: "true")
        ],
        Tags: ["data", "excel", "csv"],
        Icon: "sliders",
        Color: "#27ae60");

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var raw = context.GetString("file");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Task.FromResult(NodeExecutionResult.Failure("Dosya yuklenmedi."));
        }

        string fileName;
        byte[] bytes;
        try
        {
            var node = JsonNode.Parse(raw)!;
            fileName = node["fileName"]?.GetValue<string>() ?? "";
            bytes = Convert.FromBase64String(node["content"]?.GetValue<string>() ?? "");
        }
        catch (Exception ex)
        {
            return Task.FromResult(NodeExecutionResult.Failure($"Dosya cozumlenemedi: {ex.ToUserMessage()}"));
        }

        var hasHeader = context.GetBoolean("hasHeader", defaultValue: true);

        var isCsv = fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
        var items = isCsv
            ? ReadCsv(bytes, hasHeader)
            : ReadExcel(bytes, context.GetString("sheet"), hasHeader);

        return Task.FromResult(NodeExecutionResult.Single(items));
    }

    private static List<NodeItem> ReadExcel(byte[] bytes, string? sheetName, bool hasHeader)
    {
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = string.IsNullOrWhiteSpace(sheetName)
            ? workbook.Worksheets.First()
            : workbook.Worksheet(sheetName);

        var rows = sheet.RangeUsed()?.RowsUsed().ToList() ?? [];
        var items = new List<NodeItem>();
        if (rows.Count == 0)
        {
            return items;
        }

        var headerCells = rows[0].Cells().ToList();
        var headers = hasHeader
            ? headerCells.Select((c, i) => c.GetString() is { Length: > 0 } h ? h : $"column{i + 1}").ToList()
            : headerCells.Select((_, i) => $"column{i + 1}").ToList();

        foreach (var row in rows.Skip(hasHeader ? 1 : 0))
        {
            var obj = new JsonObject();
            var cells = row.Cells().ToList();
            for (var i = 0; i < headers.Count; i++)
            {
                obj[headers[i]] = i < cells.Count ? cells[i].GetString() : "";
            }

            items.Add(NodeItem.From(obj));
        }

        return items;
    }

    private static List<NodeItem> ReadCsv(byte[] bytes, bool hasHeader)
    {
        var items = new List<NodeItem>();
        var text = Encoding.UTF8.GetString(bytes);
        using var reader = new StringReader(text);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = hasHeader });

        if (hasHeader)
        {
            csv.Read();
            csv.ReadHeader();
            var headers = csv.HeaderRecord ?? [];
            while (csv.Read())
            {
                var obj = new JsonObject();
                foreach (var header in headers)
                {
                    obj[header] = csv.GetField(header);
                }

                items.Add(NodeItem.From(obj));
            }
        }
        else
        {
            while (csv.Read())
            {
                var obj = new JsonObject();
                for (var i = 0; i < csv.Parser.Count; i++)
                {
                    obj[$"column{i + 1}"] = csv.GetField(i);
                }

                items.Add(NodeItem.From(obj));
            }
        }

        return items;
    }
}
