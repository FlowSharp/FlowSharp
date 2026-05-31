namespace FlowSharp.Application.Nodes;

/// <summary>
/// Bir node calismasinin sonucu.her node, port basina bir item listesi
/// dondurebilir: <see cref="Outputs"/>[0] ilk cikis portu, [1] ikinci port (orn. IF "false")
/// seklindedir. Tek cikisli node'lar icin <see cref="Single"/> yardimcisini kullanin.
/// </summary>
public sealed record NodeExecutionResult
{
    private NodeExecutionResult(bool succeeded, IReadOnlyList<IReadOnlyList<NodeItem>> outputs, string? error)
    {
        Succeeded = succeeded;
        Outputs = outputs;
        Error = error;
    }

    public bool Succeeded { get; }

    /// <summary>Cikis portu basina uretilen item listeleri.</summary>
    public IReadOnlyList<IReadOnlyList<NodeItem>> Outputs { get; }

    public string? Error { get; }

    /// <summary>Ilk (varsayilan) cikis portunun item'lari.</summary>
    public IReadOnlyList<NodeItem> PrimaryItems =>
        Outputs.Count > 0 ? Outputs[0] : [];

    public static NodeExecutionResult Single(IReadOnlyList<NodeItem> items) =>
        new(true, [items], null);

    public static NodeExecutionResult Single(NodeItem item) =>
        new(true, [[item]], null);

    /// <summary>Cok portlu sonuc: her eleman bir cikis portunun item'larini temsil eder.</summary>
    public static NodeExecutionResult Multi(IReadOnlyList<IReadOnlyList<NodeItem>> outputs) =>
        new(true, outputs, null);

    public static NodeExecutionResult Failure(string error) =>
        new(false, [], error);
}
