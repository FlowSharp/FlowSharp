using System.Text;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Data;

/// <summary>
/// HTML govdesini temiz duz metne veya Markdown'a cevirir. Web scraping veya HTML e-posta
/// sonrasi, metni AI modeline temiz gondermek icin kullanilir (AngleSharp ile DOM gezerek).
/// </summary>
public sealed class HtmlToTextNode : PerItemNodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "transform.htmlToText",
        DisplayName: "HTML to Text",
        Category: NodeCategory.Data,
        Kind: NodeKind.Transform,
        Description: "HTML'i duz metne veya Markdown'a cevirir.",
        Parameters:
        [
            new NodeParameterDefinition("html", "HTML", NodeParameterType.Text, IsRequired: true,
                HelpText: "Kaynak HTML. Ornek: {{$json.body}}"),
            new NodeParameterDefinition("format", "Bicim", NodeParameterType.Select, IsRequired: true,
                DefaultValue: "text", Options: ["text", "markdown"]),
            new NodeParameterDefinition("outputField", "Cikti alani", NodeParameterType.String, DefaultValue: "text")
        ],
        Tags: ["data", "html", "text", "markdown", "ai"],
        Icon: "file-text",
        Color: "#9b59b6");

    private static readonly HtmlParser Parser = new();

    protected override async Task<NodeItem?> ProcessItemAsync(INodeExecutionContext context, NodeItem item, int index)
    {
        var html = context.GetString("html", index) ?? "";
        var format = context.GetString("format", index) ?? "text";
        var field = context.GetString("outputField", index) ?? "text";

        var document = await Parser.ParseDocumentAsync(html, context.CancellationToken);
        var body = (INode?)document.Body ?? document.DocumentElement;

        var builder = new StringBuilder();
        if (format == "markdown")
        {
            RenderMarkdown(body, builder, listDepth: 0);
        }
        else
        {
            RenderText(body, builder);
        }

        var output = (JsonObject)item.Json.DeepClone();
        output[field] = Collapse(builder.ToString());
        return NodeItem.From(output);
    }

    // Duz metin: blok elemanlarda satir sonu, script/style atilir.
    private static void RenderText(INode? node, StringBuilder builder)
    {
        if (node is null)
        {
            return;
        }

        if (node.NodeType == AngleSharp.Dom.NodeType.Text)
        {
            builder.Append(node.TextContent);
            return;
        }

        if (node is IElement element)
        {
            var tag = element.LocalName;
            if (tag is "script" or "style" or "head" or "noscript")
            {
                return;
            }

            if (tag == "br")
            {
                builder.Append('\n');
                return;
            }
        }

        foreach (var child in node.ChildNodes)
        {
            RenderText(child, builder);
        }

        if (node is IElement block && IsBlock(block.LocalName))
        {
            builder.Append('\n');
        }
    }

    // Markdown: basliklar, link, bold/italic, kod, liste ve paragraf desteklenir.
    private static void RenderMarkdown(INode? node, StringBuilder builder, int listDepth)
    {
        if (node is null)
        {
            return;
        }

        if (node.NodeType == AngleSharp.Dom.NodeType.Text)
        {
            builder.Append(node.TextContent.Replace('\n', ' '));
            return;
        }

        if (node is not IElement element)
        {
            return;
        }

        var tag = element.LocalName;
        switch (tag)
        {
            case "script" or "style" or "head" or "noscript":
                return;
            case "br":
                builder.Append("  \n");
                return;
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                builder.Append('\n').Append(new string('#', tag[1] - '0')).Append(' ');
                RenderChildren(element, builder, listDepth);
                builder.Append("\n\n");
                return;
            case "strong" or "b":
                builder.Append("**");
                RenderChildren(element, builder, listDepth);
                builder.Append("**");
                return;
            case "em" or "i":
                builder.Append('*');
                RenderChildren(element, builder, listDepth);
                builder.Append('*');
                return;
            case "code":
                builder.Append('`');
                RenderChildren(element, builder, listDepth);
                builder.Append('`');
                return;
            case "a":
                builder.Append('[');
                RenderChildren(element, builder, listDepth);
                builder.Append("](").Append(element.GetAttribute("href") ?? "").Append(')');
                return;
            case "p":
                RenderChildren(element, builder, listDepth);
                builder.Append("\n\n");
                return;
            case "ul" or "ol":
                builder.Append('\n');
                var ordered = tag == "ol";
                var number = 1;
                foreach (var li in element.Children.Where(c => c.LocalName == "li"))
                {
                    builder.Append(new string(' ', listDepth * 2));
                    builder.Append(ordered ? $"{number++}. " : "- ");
                    RenderChildren(li, builder, listDepth + 1);
                    builder.Append('\n');
                }

                builder.Append('\n');
                return;
            default:
                RenderChildren(element, builder, listDepth);
                if (IsBlock(tag))
                {
                    builder.Append('\n');
                }

                return;
        }
    }

    private static void RenderChildren(IElement element, StringBuilder builder, int listDepth)
    {
        foreach (var child in element.ChildNodes)
        {
            RenderMarkdown(child, builder, listDepth);
        }
    }

    private static bool IsBlock(string tag) =>
        tag is "div" or "p" or "section" or "article" or "header" or "footer" or "li" or "tr"
            or "table" or "blockquote" or "pre" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6";

    // Fazla bosluk ve ardisik bos satirlari tek satira indirir.
    private static string Collapse(string value)
    {
        var lines = value.Replace("\r\n", "\n").Split('\n');
        var result = new StringBuilder();
        var blankRun = 0;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            while (line.Contains("  ") && !line.EndsWith("  "))
            {
                line = line.Replace("  ", " ");
            }

            if (line.Length == 0)
            {
                if (++blankRun > 1)
                {
                    continue;
                }
            }
            else
            {
                blankRun = 0;
            }

            result.Append(line).Append('\n');
        }

        return result.ToString().Trim();
    }
}
