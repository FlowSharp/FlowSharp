using System.Text.Json.Nodes;

namespace FlowSharp.Application.Nodes.Expressions;

/// <summary>
/// <c>{{ ... }}</c> ifadelerini cozer. Desteklenen referanslar:
/// <c>$json</c> (mevcut item), <c>$node["Ad"].json</c> (baska node ciktisi),
/// <c>$now</c>, <c>$today</c>, <c>$itemIndex</c>, <c>$runIndex</c>, <c>$trigger</c>.
/// </summary>
public interface IExpressionEvaluator
{
    /// <summary>Sablonu metne cevirir; birden cok ifade ve duz metin karisik olabilir.</summary>
    string EvaluateToString(string? template, ExpressionContext context);

    /// <summary>
    /// Sablon tek bir <c>{{ ... }}</c> ifadesinden ibaretse sonucu ham JSON dugumu olarak dondurur
    /// (sayi/obje/dizi korunur). Aksi halde metin sonucu bir JsonValue olarak sarilir.
    /// </summary>
    JsonNode? EvaluateToNode(string? template, ExpressionContext context);

    /// <summary>Metinde en az bir <c>{{ ... }}</c> ifadesi var mi?</summary>
    bool ContainsExpression(string? template);
}
