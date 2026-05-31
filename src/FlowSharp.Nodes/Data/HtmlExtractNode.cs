using System.Text.Json.Nodes;
using AngleSharp.Html.Parser;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Data;

/// <summary>
/// HTML icinden CSS selector ile veri cikarir (AngleSharp). Web scraping / HTTP Request
/// sonrasi gelen HTML govdesinden alan ayiklama icin idealdir.
/// </summary>
public sealed class HtmlExtractNode : PerItemNodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "transform.htmlExtract",
        DisplayName: "HTML Extract",
        Category: NodeCategory.Data,
        Kind: NodeKind.Transform,
        Description: "HTML'den CSS selector ile deger cikarir.",
        Parameters:
        [
            new NodeParameterDefinition("html", "HTML", NodeParameterType.Text, IsRequired: true,
                HelpText: "Kaynak HTML. Ornek: {{$json.body}}"),
            new NodeParameterDefinition("selector", "CSS Selector", NodeParameterType.String, IsRequired: true,
                HelpText: "Ornek: h1, .price, a#link"),
            new NodeParameterDefinition("property", "Deger", NodeParameterType.Select, IsRequired: true,
                DefaultValue: "text", Options: ["text", "html", "attribute"]),
            new NodeParameterDefinition("attribute", "Attribute", NodeParameterType.String,
                HelpText: "property=attribute ise: ornek href, src"),
            new NodeParameterDefinition("all", "Tum eslesmeler?", NodeParameterType.Boolean, DefaultValue: "false",
                HelpText: "true ise eslesmeler bir dizi olarak doner."),
            new NodeParameterDefinition("outputField", "Cikti alani", NodeParameterType.String, DefaultValue: "value")
        ],
        Tags: ["data", "html", "scraping"],
        Icon: "globe",
        Color: "#e67e22");

    private static readonly HtmlParser Parser = new();

    protected override async Task<NodeItem?> ProcessItemAsync(INodeExecutionContext context, NodeItem item, int index)
    {
        var html = context.GetString("html", index) ?? "";
        var selector = context.GetString("selector", index) ?? "";
        var property = context.GetString("property", index) ?? "text";
        var attribute = context.GetString("attribute", index) ?? "";
        var all = context.GetBoolean("all", index);
        var field = context.GetString("outputField", index) ?? "value";

        var document = await Parser.ParseDocumentAsync(html, context.CancellationToken);
        var output = (JsonObject)item.Json.DeepClone();

        if (all)
        {
            var array = new JsonArray();
            foreach (var element in document.QuerySelectorAll(selector))
            {
                array.Add(Extract(element, property, attribute));
            }

            output[field] = array;
        }
        else
        {
            var element = document.QuerySelector(selector);
            output[field] = element is null ? null : Extract(element, property, attribute);
        }

        return NodeItem.From(output);
    }

    private static JsonNode? Extract(AngleSharp.Dom.IElement element, string property, string attribute) =>
        property switch
        {
            "html" => element.InnerHtml,
            "attribute" => element.GetAttribute(attribute),
            _ => element.TextContent.Trim()
        };
}
