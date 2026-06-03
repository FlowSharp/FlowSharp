using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using FlowSharp.Application.Nodes.Expressions;

namespace FlowSharp.Infrastructure.Workflows.Expressions;

/// <summary>
/// <c>{{ ... }}</c> ifadelerini cozen hafif degerlendirici.
/// Tam bir JS motoru degildir; referans (path) erisimini ve birkac yardimci degeri destekler.
/// Ileride gercek bir JS sandbox (Jint) ile genisletilebilir.
/// </summary>
public sealed class ExpressionEvaluator : IExpressionEvaluator
{
    public bool ContainsExpression(string? template) =>
        !string.IsNullOrEmpty(template) && template.Contains("{{") && template.Contains("}}");

    public string EvaluateToString(string? template, ExpressionContext context)
    {
        if (string.IsNullOrEmpty(template) || !ContainsExpression(template))
        {
            return template ?? string.Empty;
        }

        var builder = new StringBuilder(template.Length);
        var index = 0;

        while (index < template.Length)
        {
            var open = template.IndexOf("{{", index, StringComparison.Ordinal);
            if (open < 0)
            {
                builder.Append(template, index, template.Length - index);
                break;
            }

            builder.Append(template, index, open - index);
            var close = template.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                builder.Append(template, open, template.Length - open);
                break;
            }

            var expression = template.Substring(open + 2, close - open - 2).Trim();
            var value = Evaluate(expression, context);
            builder.Append(Stringify(value));
            index = close + 2;
        }

        return builder.ToString();
    }

    public JsonNode? EvaluateToNode(string? template, ExpressionContext context)
    {
        if (string.IsNullOrEmpty(template) || !ContainsExpression(template))
        {
            return template is null ? null : JsonValue.Create(template);
        }

        var trimmed = template.Trim();
        var open = trimmed.IndexOf("{{", StringComparison.Ordinal);
        var close = trimmed.LastIndexOf("}}", StringComparison.Ordinal);

        // Sablon tek bir ifadeden ibaretse ham dugumu koru (sayi/obje/dizi).
        if (open == 0 && close == trimmed.Length - 2)
        {
            var inner = trimmed[(open + 2)..close];
            // Ic kisimda baska bir "{{" yoksa tek ifadedir.
            if (!inner.Contains("{{", StringComparison.Ordinal))
            {
                return Evaluate(inner.Trim(), context);
            }
        }

        return JsonValue.Create(EvaluateToString(template, context));
    }

    private static JsonNode Evaluate(string expression, ExpressionContext context) =>
        EvaluateReference(expression, context)
            ?? throw new ExpressionEvaluationException("Ifade cozulemedi (alan bulunamadi veya gecersiz).");

    private static JsonNode? EvaluateReference(string expression, ExpressionContext context)
    {
        var tokens = Tokenize(expression);
        if (tokens.Count == 0)
        {
            return null;
        }

        var root = tokens[0];
        JsonNode? current;
        var accessorStart = 1;

        switch (root)
        {
            case "$json":
                current = context.CurrentItem?.Json;
                break;
            case "$item":
                current = WrapItem(context.CurrentItem?.Json);
                break;
            case "$trigger":
                current = context.Trigger;
                break;
            case "$now":
                return JsonValue.Create(DateTimeOffset.UtcNow.ToString("O"));
            case "$today":
                return JsonValue.Create(DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"));
            case "$itemIndex":
                return JsonValue.Create(context.ItemIndex);
            case "$runIndex":
                return JsonValue.Create(context.RunIndex);
            case "$node":
                // $node ["Name"] ...
                if (tokens.Count < 2)
                {
                    return null;
                }
                var nodeName = tokens[1];
                var items = context.NodeOutputs.TryGetValue(nodeName, out var nodeItems) ? nodeItems : null;
                var item = items is { Count: > 0 }
                    ? items[Math.Min(context.ItemIndex, items.Count - 1)]
                    : null;
                current = WrapItem(item?.Json);
                accessorStart = 2;
                break;
            default:
                return null;
        }

        for (var i = accessorStart; i < tokens.Count && current is not null; i++)
        {
            current = Navigate(current, tokens[i]);
        }

        return current;
    }

    private static JsonObject WrapItem(JsonNode? itemJson) =>
        new() { ["json"] = itemJson?.DeepClone() ?? new JsonObject() };

    private static JsonNode? Navigate(JsonNode node, string accessor)
    {
        if (node is JsonObject obj)
        {
            return obj.TryGetPropertyValue(accessor, out var value) ? value : null;
        }

        if (node is JsonArray array && int.TryParse(accessor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
        {
            return idx >= 0 && idx < array.Count ? array[idx] : null;
        }

        return null;
    }

    /// <summary>
    /// Ifadeyi token'lara ayirir: ilk token kok ($json, $node ...), sonrakiler
    /// property adlari / dizi indeksleridir. Hem nokta hem koseli parantez sozdizimini destekler.
    /// </summary>
    private static List<string> Tokenize(string expression)
    {
        var tokens = new List<string>();
        var index = 0;
        var length = expression.Length;

        while (index < length)
        {
            var ch = expression[index];

            if (ch is ' ' or '.')
            {
                index++;
                continue;
            }

            if (ch == '[')
            {
                index++;
                if (index < length && (expression[index] == '"' || expression[index] == '\''))
                {
                    var quote = expression[index++];
                    var start = index;
                    while (index < length && expression[index] != quote)
                    {
                        index++;
                    }
                    tokens.Add(expression[start..index]);
                    index++; // closing quote
                    while (index < length && expression[index] != ']')
                    {
                        index++;
                    }
                    index++; // closing bracket
                }
                else
                {
                    var start = index;
                    while (index < length && expression[index] != ']')
                    {
                        index++;
                    }
                    tokens.Add(expression[start..index].Trim());
                    index++; // closing bracket
                }
            }
            else
            {
                var start = index;
                while (index < length && expression[index] is not ('.' or '[' or ' '))
                {
                    index++;
                }
                tokens.Add(expression[start..index]);
            }
        }

        return tokens;
    }

    private static string Stringify(JsonNode? node)
    {
        if (node is null)
        {
            return string.Empty;
        }

        if (node is JsonValue value)
        {
            return value.ToString();
        }

        return node.ToJsonString();
    }
}
