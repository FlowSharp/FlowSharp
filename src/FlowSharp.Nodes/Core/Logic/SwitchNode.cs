using System.Globalization;
using System.Text.Json.Nodes;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Core.Logic;

/// <summary>
/// Item'lari kurallara gore en fazla 4 cikisa yonlendirir. "rules" parametresi
/// [{"value":"A","output":0}, ...] formatinda bir JSON dizisidir; value1 ile esit
/// olan ilk kuralin output portuna gonderilir.
/// Hicbir kurala uymayan item'lar ayri bir "fallback" cikisina gider (sessizce dusurulmez).
/// Eslesip de gecersiz/aralik disi bir output tasiyan kural ise sessizce gizlenmez; node
/// acik bir hata ile basarisiz olur (yapilandirma hatasi).
/// </summary>
public sealed class SwitchNode : NodeType
{
    private const int OutputCount = 4;
    private const int FallbackPort = OutputCount; // 4: eslesmeyen item'larin gittigi ayri cikis.

    public override NodeDefinition Definition { get; } = new(
        Key: "switch.condition",
        DisplayName: "Switch",
        Category: NodeCategory.Core,
        Kind: NodeKind.Condition,
        Description: "Esleyen kurala gore item'lari farkli cikislara yonlendirir.",
        Parameters:
        [
            new NodeParameterDefinition("value1", "Value", NodeParameterType.String, IsRequired: true,
                HelpText: "Ornek: {{$json.type}}"),
            new NodeParameterDefinition("rules", "Rules (JSON)", NodeParameterType.Json,
                DefaultValue: "[{\"value\":\"a\",\"output\":0},{\"value\":\"b\",\"output\":1}]",
                HelpText: "Her kural {\"value\":\"...\",\"output\":0-3,\"label\":\"opsiyonel cikis adi\"}. 'label' verirsen cikis portu o adla, vermezsen 'value' ile gosterilir. Eslesmeyenler Fallback'e gider.")
        ],
        Tags: ["core", "logic"],
        Icon: "bezier2",
        Color: "#408000",
        Outputs:
        [
            NodePort.Named("0", "Output 0"),
            NodePort.Named("1", "Output 1"),
            NodePort.Named("2", "Output 2"),
            NodePort.Named("3", "Output 3"),
            NodePort.Named("fallback", "Fallback (eslesmeyen)")
        ]);

    public override Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var outputs = new List<NodeItem>[OutputCount + 1];
        for (var i = 0; i < outputs.Length; i++)
        {
            outputs[i] = [];
        }

        var items = context.Items.Count > 0 ? context.Items : [NodeItem.Empty()];

        for (var index = 0; index < items.Count; index++)
        {
            var value1 = context.GetString("value1", index) ?? string.Empty;
            var route = Resolve(context.GetJson("rules", index), value1);

            switch (route.Kind)
            {
                case RouteKind.Matched:
                    outputs[route.Port].Add(items[index]);
                    break;
                case RouteKind.Unmatched:
                    outputs[FallbackPort].Add(items[index]);
                    break;
                case RouteKind.InvalidOutput:
                    return Task.FromResult(NodeExecutionResult.Failure(
                        $"Switch kuralinda gecersiz 'output': {route.RawOutput}. Gecerli bir port indeksi (0..{OutputCount - 1}) olmali."));
            }
        }

        return Task.FromResult(NodeExecutionResult.Multi(outputs));
    }

    private static Resolution Resolve(JsonNode? rules, string value1)
    {
        if (rules is not JsonArray array)
        {
            return Resolution.Unmatched;
        }

        foreach (var rule in array.OfType<JsonObject>())
        {
            var ruleValue = rule.TryGetPropertyValue("value", out var v) ? v?.ToString() : null;
            if (!string.Equals(ruleValue, value1, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Eslesen kural: "output" yoksa ilk cikis (0) varsayilir; varsa gecerli bir port olmali.
            if (!rule.TryGetPropertyValue("output", out var o) || o is null)
            {
                return Resolution.Matched(0);
            }

            var port = ParseOutput(o);
            return port is >= 0 and < OutputCount
                ? Resolution.Matched(port.Value)
                : Resolution.Invalid(o.ToJsonString());
        }

        return Resolution.Unmatched;
    }

    /// <summary>
    /// Kural "output" degerini port indeksine cevirir. Sayi olabilecegi gibi (JSON elle
    /// duzenlendiginde veya form round-trip'inde) string ("1") de olabilir; ikisi de kabul edilir.
    /// Cevrilemezse <c>null</c> doner (cagiran bunu yapilandirma hatasi olarak ele alir).
    /// </summary>
    private static int? ParseOutput(JsonNode? output)
    {
        if (output is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out var port))
        {
            return port;
        }

        return value.TryGetValue<string>(out var text) &&
               int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private enum RouteKind
    {
        Matched,
        Unmatched,
        InvalidOutput
    }

    private readonly record struct Resolution(RouteKind Kind, int Port, string? RawOutput)
    {
        public static readonly Resolution Unmatched = new(RouteKind.Unmatched, 0, null);

        public static Resolution Matched(int port) => new(RouteKind.Matched, port, null);

        public static Resolution Invalid(string raw) => new(RouteKind.InvalidOutput, 0, raw);
    }
}
