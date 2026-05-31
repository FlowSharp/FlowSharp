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

        switch (operation)
        {
            case "isEmpty":
                return string.IsNullOrEmpty(left);
            case "isNotEmpty":
                return !string.IsNullOrEmpty(left);
            case "isTrue":
                return bool.TryParse(left, out var bt) && bt;
            case "isFalse":
                return bool.TryParse(left, out var bf) && !bf;
            case "equals":
                return NumericOr(left, right, (a, b) => a == b, () => string.Equals(left, right, StringComparison.Ordinal));
            case "notEquals":
                return NumericOr(left, right, (a, b) => a != b, () => !string.Equals(left, right, StringComparison.Ordinal));
            case "contains":
                return left.Contains(right, StringComparison.OrdinalIgnoreCase);
            case "notContains":
                return !left.Contains(right, StringComparison.OrdinalIgnoreCase);
            case "startsWith":
                return left.StartsWith(right, StringComparison.OrdinalIgnoreCase);
            case "endsWith":
                return left.EndsWith(right, StringComparison.OrdinalIgnoreCase);
            case "greaterThan":
                return Numeric(left, right, (a, b) => a > b);
            case "lessThan":
                return Numeric(left, right, (a, b) => a < b);
            case "greaterOrEqual":
                return Numeric(left, right, (a, b) => a >= b);
            case "lessOrEqual":
                return Numeric(left, right, (a, b) => a <= b);
            default:
                return false;
        }
    }

    private static bool Numeric(string left, string right, Func<double, double, bool> compare) =>
        TryNum(left, out var a) && TryNum(right, out var b) && compare(a, b);

    private static bool NumericOr(string left, string right, Func<double, double, bool> numeric, Func<bool> textual) =>
        TryNum(left, out var a) && TryNum(right, out var b) ? numeric(a, b) : textual();

    private static bool TryNum(string value, out double number) =>
        double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out number);
}
