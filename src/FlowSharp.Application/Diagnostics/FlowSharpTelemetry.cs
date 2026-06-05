using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FlowSharp.Application.Diagnostics;

/// <summary>
/// Uygulama geneli OpenTelemetry kaynaklari: dagitik izleme (trace) icin <see cref="ActivitySource"/>
/// ve metrikler icin sayac/histogram. Isimler altyapidaki OTel kayit kodu tarafindan abone olunur.
/// BCL tipleri kullanir; OTel paketine bagimli degildir (her katmandan referans verilebilir).
/// </summary>
public static class FlowSharpTelemetry
{
    public const string ActivitySourceName = "FlowSharp.Workflows";
    public const string MeterName = "FlowSharp.Workflows";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);

    /// <summary>Toplam workflow calistirma sayisi (<c>status</c> etiketli: succeeded/failed).</summary>
    public static readonly Counter<long> WorkflowRuns = Meter.CreateCounter<long>(
        "flowsharp.workflow.runs", unit: "{run}", description: "Toplam workflow calistirma sayisi.");

    /// <summary>Workflow calistirma suresi, milisaniye (<c>status</c> etiketli).</summary>
    public static readonly Histogram<double> WorkflowDuration = Meter.CreateHistogram<double>(
        "flowsharp.workflow.duration", unit: "ms", description: "Workflow calistirma suresi.");
}
