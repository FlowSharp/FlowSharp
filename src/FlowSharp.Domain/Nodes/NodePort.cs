namespace FlowSharp.Domain.Nodes;

/// <summary>
/// Bir node'un giris/cikis baglanti noktasi.
/// portuna sahiptir; IF iki ("true"/"false"), Switch ise N cikis portuna sahiptir.
/// </summary>
public sealed record NodePort(string Name, string Label, NodePortType Type = NodePortType.Main)
{
    public static readonly NodePort Main = new("main", "Main");

    public static NodePort Named(string name, string label) => new(name, label);
}

public enum NodePortType
{
    Main = 0,
    AiTool = 1,
    AiMemory = 2,
    AiModel = 3
}
