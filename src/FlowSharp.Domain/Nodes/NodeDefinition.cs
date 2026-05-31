namespace FlowSharp.Domain.Nodes;

/// <summary>
/// Bir node tipinin UI ve motorun ihtiyac duydugu tum meta verisini tasir.
/// <c>INodeTypeDescription</c> karsiligidir; tek bir node sinifi
/// kendi <see cref="NodeDefinition"/> ornegini dondurerek hem palette gorunur
/// hem de calistirilabilir hale gelir.
/// </summary>
public sealed record NodeDefinition(
    string Key,
    string DisplayName,
    NodeCategory Category,
    NodeKind Kind,
    string Description,
    IReadOnlyList<NodeParameterDefinition> Parameters,
    IReadOnlyList<string> Tags,
    string Icon,
    bool IsAiPowered = false,
    string Color = "#6b7280",
    int Version = 1,
    IReadOnlyList<NodePort>? Inputs = null,
    IReadOnlyList<NodePort>? Outputs = null,
    IReadOnlyList<string>? Credentials = null)
{
    /// <summary>Giris portlari. Bos birakilirsa tek bir "main" giris kabul edilir (trigger'larda bos olabilir).</summary>
    public IReadOnlyList<NodePort> InputPorts => Inputs ?? (Kind == NodeKind.Trigger ? [] : [NodePort.Main]);

    /// <summary>Cikis portlari. Bos birakilirsa tek bir "main" cikis kullanilir.</summary>
    public IReadOnlyList<NodePort> OutputPorts => Outputs ?? [NodePort.Main];

    /// <summary>Bu node'a baglanmis credential tip anahtarlari (orn. "httpBasicAuth").</summary>
    public IReadOnlyList<string> CredentialKeys => Credentials ?? [];

    /// <summary>Ana akis (sol) giris portlari.</summary>
    public IReadOnlyList<NodePort> MainInputPorts => InputPorts.Where(port => port.Type == NodePortType.Main).ToList();

    /// <summary>Ana akis (sag) cikis portlari.</summary>
    public IReadOnlyList<NodePort> MainOutputPorts => OutputPorts.Where(port => port.Type == NodePortType.Main).ToList();

    /// <summary>AI alt-node girisleri (agent'in altinda: Model/Tool/Memory).</summary>
    public IReadOnlyList<NodePort> SubInputPorts => InputPorts.Where(port => port.Type != NodePortType.Main).ToList();

    /// <summary>Bu node'un sagladigi AI alt-cikisi (orn. dil modeli, arac).</summary>
    public IReadOnlyList<NodePort> SubOutputPorts => OutputPorts.Where(port => port.Type != NodePortType.Main).ToList();

    /// <summary>Bu node bir AI alt-node mu (agent'a baglanan model/arac/hafiza)?</summary>
    public bool IsSubNode => OutputPorts.Any(port => port.Type != NodePortType.Main);
}
