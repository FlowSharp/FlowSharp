using System.Globalization;

namespace FlowSharp.Nodes.Core.Logic;

/// <summary>IF, Filter ve Switch node'larinin ortak kosul karsilastirma mantigi.</summary>
internal static class ConditionEvaluator
{
    public static readonly IReadOnlyList<string> Operations =
    [
        "equals", "notEquals", "contains", "notContains", "startsWith", "endsWith",
        "isEmpty", "isNotEmpty", "greaterThan", "lessThan", "greaterOrEqual", "lessOrEqual",
        "isTrue", "isFalse"
    ];

    public static bool Evaluate(string? left, string operation, string? right)
    {
        left ??= string.Empty;
        right ??= string.Empty;

        return operation switch
        {
            "isEmpty" => string.IsNullOrEmpty(left),
            "isNotEmpty" => !string.IsNullOrEmpty(left),
            "isTrue" => bool.TryParse(left, out var bt) && bt,
            "isFalse" => bool.TryParse(left, out var bf) && !bf,
            "equals" => NumericOr(left, right, (a, b) => a == b, () => string.Equals(left, right, StringComparison.Ordinal)),
            "notEquals" => NumericOr(left, right, (a, b) => a != b, () => !string.Equals(left, right, StringComparison.Ordinal)),
            "contains" => left.Contains(right, StringComparison.OrdinalIgnoreCase),
            "notContains" => !left.Contains(right, StringComparison.OrdinalIgnoreCase),
            "startsWith" => left.StartsWith(right, StringComparison.OrdinalIgnoreCase),
            "endsWith" => left.EndsWith(right, StringComparison.OrdinalIgnoreCase),
            "greaterThan" => Numeric(left, right, (a, b) => a > b),
            "lessThan" => Numeric(left, right, (a, b) => a < b),
            "greaterOrEqual" => Numeric(left, right, (a, b) => a >= b),
            "lessOrEqual" => Numeric(left, right, (a, b) => a <= b),
            _ => false,
        };
    }

    private static bool Numeric(string left, string right, Func<double, double, bool> compare) =>
        TryNum(left, out var a) && TryNum(right, out var b) && compare(a, b);

    private static bool NumericOr(string left, string right, Func<double, double, bool> numeric, Func<bool> textual) =>
        TryNum(left, out var a) && TryNum(right, out var b) ? numeric(a, b) : textual();

    private static bool TryNum(string value, out double number) =>
        double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out number);
}
