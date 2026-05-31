using System.Text.Json.Nodes;
using System.Xml.Linq;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Data;

/// <summary>
/// XML &lt;-&gt; JSON donusturur. Kurumsal eski sistemler (banka, muhasebe, SOAP servisleri)
/// hala XML ile calisir; bu node gelen XML'i JSON'a veya item JSON'unu XML'e cevirir.
/// </summary>
public sealed class XmlJsonNode : PerItemNodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "transform.xmlJson",
        DisplayName: "XML <-> JSON",
        Category: NodeCategory.Data,
        Kind: NodeKind.Transform,
        Description: "XML'i JSON'a veya JSON'i XML'e donusturur.",
        Parameters:
        [
            new NodeParameterDefinition("mode", "Yon", NodeParameterType.Select, IsRequired: true,
                DefaultValue: "xmlToJson", Options: ["xmlToJson", "jsonToXml"]),
            new NodeParameterDefinition("xml", "XML", NodeParameterType.Text,
                HelpText: "mode=xmlToJson ise kaynak XML. Ornek: {{$json.body}}"),
            new NodeParameterDefinition("sourceField", "Kaynak alan", NodeParameterType.String,
                HelpText: "mode=jsonToXml ise donusturulecek alan. Bos ise tum item JSON'u kullanilir."),
            new NodeParameterDefinition("rootElement", "Kok eleman", NodeParameterType.String, DefaultValue: "root",
                HelpText: "mode=jsonToXml icin XML kok eleman adi."),
            new NodeParameterDefinition("outputField", "Cikti alani", NodeParameterType.String, DefaultValue: "value")
        ],
        Tags: ["data", "xml", "json", "convert"],
        Icon: "code",
        Color: "#16a085");

    protected override Task<NodeItem?> ProcessItemAsync(INodeExecutionContext context, NodeItem item, int index)
    {
        var mode = context.GetString("mode", index) ?? "xmlToJson";
        var field = context.GetString("outputField", index) ?? "value";
        var output = (JsonObject)item.Json.DeepClone();

        if (mode == "jsonToXml")
        {
            var sourceField = context.GetString("sourceField", index);
            JsonNode? source = string.IsNullOrWhiteSpace(sourceField) ? item.Json : item.Json[sourceField];
            var root = context.GetString("rootElement", index);
            root = string.IsNullOrWhiteSpace(root) ? "root" : root;
            var element = JsonToXml(source, root);
            output[field] = element.ToString();
        }
        else
        {
            var xml = context.GetString("xml", index) ?? "";
            var document = XDocument.Parse(xml);
            output[field] = XmlToJson(document.Root!);
        }

        return Task.FromResult<NodeItem?>(NodeItem.From(output));
    }

    // XElement -> JsonNode. Tek metin icerigi olan elemanlar dogrudan deger; aksi halde
    // attribute'lar "@ad", tekrarlanan cocuk elemanlar dizi olur.
    private static JsonNode? XmlToJson(XElement element)
    {
        var hasChildren = element.HasElements;
        var hasAttributes = element.HasAttributes;

        if (!hasChildren && !hasAttributes)
        {
            return element.Value;
        }

        var obj = new JsonObject();
        foreach (var attribute in element.Attributes())
        {
            obj[$"@{attribute.Name.LocalName}"] = attribute.Value;
        }

        foreach (var group in element.Elements().GroupBy(child => child.Name.LocalName))
        {
            var items = group.ToList();
            if (items.Count == 1)
            {
                obj[group.Key] = XmlToJson(items[0]);
            }
            else
            {
                var array = new JsonArray();
                foreach (var child in items)
                {
                    array.Add(XmlToJson(child));
                }

                obj[group.Key] = array;
            }
        }

        if (hasChildren is false && hasAttributes && !string.IsNullOrWhiteSpace(element.Value))
        {
            obj["#text"] = element.Value;
        }

        return obj;
    }

    // JsonNode -> XElement. Nesneler cocuk eleman, diziler tekrarlanan eleman olur.
    private static XElement JsonToXml(JsonNode? node, string name)
    {
        var element = new XElement(SafeName(name));
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (key.StartsWith('@'))
                    {
                        element.SetAttributeValue(key[1..], value?.ToString());
                    }
                    else if (key == "#text")
                    {
                        element.Add(value?.ToString());
                    }
                    else if (value is JsonArray array)
                    {
                        foreach (var entry in array)
                        {
                            element.Add(JsonToXml(entry, key));
                        }
                    }
                    else
                    {
                        element.Add(JsonToXml(value, key));
                    }
                }

                break;

            case JsonArray array:
                foreach (var entry in array)
                {
                    element.Add(JsonToXml(entry, "item"));
                }

                break;

            case JsonValue value:
                element.Add(value.ToString());
                break;
        }

        return element;
    }

    private static string SafeName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0 || !(char.IsLetter(trimmed[0]) || trimmed[0] == '_'))
        {
            return "node";
        }

        return trimmed;
    }
}
