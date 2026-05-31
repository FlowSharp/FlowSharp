namespace FlowSharp.Application.Workflows;

/// <summary>
/// appsettings.json "Executions" bolumu. Calisma kayitlarinin ne kadarinin DB'ye yazilacagini
/// ve ne kadar saklanacagini yonetir. Agir node ciktilari cogu zaman yalniz o anki incelemede
/// ise yarar; canli izleme zaten Redis stream'inden gelir. Bu yuzden veriyi yazmamak (None)
/// metadata'yi korurken DB sismesini onler.
/// </summary>
public sealed class ExecutionOptions
{
    public const string SectionName = "Executions";

    /// <summary>Node ciktilarinin (agir veri) saklanmasi: "All" | "ErrorsOnly" | "None". Metadata her zaman saklanir.</summary>
    public string SaveData { get; set; } = "All";

    /// <summary>Workflow basina saklanacak azami calisma sayisi (0 = sinirsiz).</summary>
    public int MaxCount { get; set; } = 1000;

    /// <summary>Calisma kayitlarinin azami yasi, gun (0 = sinirsiz).</summary>
    public int MaxAgeDays { get; set; } = 30;
}
