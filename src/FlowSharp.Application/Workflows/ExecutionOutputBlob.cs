using System.Text.Json;

namespace FlowSharp.Application.Workflows;

/// <summary>
/// Offload edilmis execution ciktilari icin isaretci (marker) bicimi. DB'deki Output sutunu, asil
/// icerik blob deposundayken yalniz <c>{"_blobRef":"&lt;key&gt;"}</c> tutar. Okuma yolunda bu isaretci
/// tespit edilip icerik blob'dan geri yuklenir (rehydrate).
/// </summary>
public static class ExecutionOutputBlob
{
    private const string RefProperty = "_blobRef";

    /// <summary>Verilen blob referansi icin isaretci JSON belgesi olusturur.</summary>
    public static JsonDocument CreateMarker(string blobReference) =>
        JsonDocument.Parse($$"""{"{{RefProperty}}":{{JsonSerializer.Serialize(blobReference)}}}""");

    /// <summary>Belge bir offload isaretcisi ise blob referansini doner; degilse <c>false</c>.</summary>
    public static bool TryGetReference(JsonDocument? document, out string reference)
    {
        reference = string.Empty;
        if (document is null)
        {
            return false;
        }

        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(RefProperty, out var refElement) &&
            refElement.ValueKind == JsonValueKind.String)
        {
            reference = refElement.GetString() ?? string.Empty;
            return reference.Length > 0;
        }

        return false;
    }
}
