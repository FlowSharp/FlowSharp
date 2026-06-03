namespace FlowSharp.Application.Nodes.Expressions;

/// <summary>Expression cozumlenemediginde workflow calismasini durdurmak icin kullanilir.</summary>
public sealed class ExpressionEvaluationException(string message) : Exception(message);
