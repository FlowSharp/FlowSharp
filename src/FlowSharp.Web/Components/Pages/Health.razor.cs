using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MudBlazor;

namespace FlowSharp.Web.Components.Pages;

public partial class Health
{
    [Inject] public HealthCheckService HealthCheckService { get; set; } = default!;

    private HealthReport? report;
    private DateTime lastChecked;
    private bool loading;

    protected override async Task OnInitializedAsync() => await ReloadAsync();

    private async Task ReloadAsync()
    {
        loading = true;
        try
        {
            report = await HealthCheckService.CheckHealthAsync();
            lastChecked = DateTime.Now;
        }
        finally
        {
            loading = false;
        }
    }

    private Severity OverallSeverity => report?.Status switch
    {
        HealthStatus.Healthy => Severity.Success,
        HealthStatus.Degraded => Severity.Warning,
        _ => Severity.Error
    };

    private static Color StatusColor(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => Color.Success,
        HealthStatus.Degraded => Color.Warning,
        _ => Color.Error
    };

    private static string StatusText(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => "Sağlıklı",
        HealthStatus.Degraded => "Kısmi",
        _ => "Sorunlu"
    };
}
