using System.Text.Json;
using FlowSharp.Application.Nodes;

namespace FlowSharp.Application.Workflows;

/// <summary>
/// Workflow tanimini (JSONB) graf olarak yorumlayip node'lari baglantilara gore
/// calistiran motor. Trigger payload'i baslangic item'larini olusturur.
/// </summary>
public interface IWorkflowExecutionEngine
{
    Task<WorkflowRunResult> ExecuteAsync(
        JsonElement definition,
        JsonElement triggerPayload,
        WorkflowExecutionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir node'un dinamik parametre seceneklerini uretir (UI dropdown'lari icin).
    /// Hedef node'un upstream zinciri calistirilir, ardindan node
    /// <see cref="IHasDynamicOptions"/> implemente ediyorsa secenekler okunur.
    /// Hedef node'un kendisi CALISTIRILMAZ (yan etki olmaz).
    /// </summary>
    Task<IReadOnlyList<NodeParameterOption>> LoadOptionsAsync(
        JsonElement definition,
        string nodeId,
        string parameterKey,
        string? actorOwnerId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Calistirma secenekleri: belirli bir node'dan baslatma, canli ilerleme bildirimi vb.</summary>
public sealed class WorkflowExecutionOptions
{
    /// <summary>Yalniz bu node ornek adindan baslat (manuel tek-node testi); null ise tum tetikleyiciler.</summary>
    public string? StartNodeName { get; init; }

    /// <summary>
    /// true ise bagli trigger bulunmadiginda girisi olmayan kaynak node'lardan baslatmaya izin verir.
    /// Yalniz designer dropdown upstream hesaplamasi gibi ic/yardimci calistirmalarda kullanilmali.
    /// </summary>
    public bool AllowSourceNodesWithoutTrigger { get; init; }

    /// <summary>Her node tamamlandiginda cagrilir (SignalR canli durum icin).</summary>
    public Func<NodeRunData, Task>? OnNodeCompleted { get; init; }

    /// <summary>AI modelinden gelen metin parcalarini canli chat UI'ina aktarmak icin kullanilir.</summary>
    public Func<string, Task>? OnTextDelta { get; init; }

    /// <summary>Calisan workflow'un kimligi (RAG gibi workspace'e ozel kaynaklari izole etmek icin).</summary>
    public Guid? WorkflowId { get; init; }

    /// <summary>Bu calismayi yetkilendiren sahibin (workflow owner / tetikleyen kullanici) Identity Id'si.
    /// Credential cozumlemede sahiplik dogrulamasi icin kullanilir; yalniz ayni sahibe ait credential
    /// cozulur. null = sistem/eski (yalniz sahipsiz credential'lar).</summary>
    public string? ActorOwnerId { get; init; }

    /// <summary>
    /// <c>true</c> (varsayilan): node ciktilari <see cref="NodeRunData.Output"/> ve nihai sonuca
    /// kopyalanir (webhook yaniti, designer canli izleme, kalici kayit icin gerekli).
    /// <c>false</c>: yalniz metadata tutulur, agir DeepClone'lar atlanir; veri AKISI etkilenmez.
    /// Kuyruk-worker + SaveData=None gibi ciktinin okunmadigi yollarda kullanilir.
    /// </summary>
    public bool CaptureData { get; init; } = true;
}
