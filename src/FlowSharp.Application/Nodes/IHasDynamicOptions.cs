namespace FlowSharp.Application.Nodes;

/// <summary>Bir parametre icin dinamik olarak uretilen tek bir secenek (dropdown ogesi).</summary>
public sealed record NodeParameterOption(string Value, string Label);

/// <summary>
/// Bir parametresinin secenekleri calisma zamaninda (upstream baglanti/credential'a gore)
/// uretilen node'lar bu arayuzu implemente eder. UI, parametreyi
/// <see cref="FlowSharp.Domain.Nodes.NodeParameterDefinition.DynamicOptions"/> isaretliyse
/// generic olarak bu metodu cagirir; node'a ozel UI kodu gerekmez.
/// </summary>
public interface IHasDynamicOptions
{
    /// <summary>
    /// Verilen parametre icin secenekleri uretir. <paramref name="context"/> upstream item'lari
    /// (orn. baglanti state'i), node parametrelerini ve credential erisimini saglar.
    /// </summary>
    Task<IReadOnlyList<NodeParameterOption>> LoadOptionsAsync(
        INodeExecutionContext context,
        string parameterKey);
}
