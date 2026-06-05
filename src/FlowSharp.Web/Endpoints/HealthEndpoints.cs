using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using FlowSharp.Application.Json;
using FlowSharp.Infrastructure.Identity;

namespace FlowSharp.Web.Endpoints;

/// <summary>
/// Health check endpoint'leri (iki katmanli):
/// - <c>/health/live</c>  : liveness probe. Sadece process ayakta mi? Anonim, minimal.
/// - <c>/health/ready</c> : readiness probe. Kritik bagimliliklar (DB) hazir mi? Anonim, minimal.
/// - <c>/health</c>       : detayli bilesen kirilimi. Yalniz admin (hassas detay sizmasin).
/// Probe'lar anonimdir cunku orkestrator/LB kimlik dogrulayamaz; detayli rapor admin'e kapalidir.
/// </summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // Liveness: hicbir kontrol calistirma; endpoint yanit veriyorsa process ayaktadir.
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        }).AllowAnonymous();

        // Readiness: "ready" etiketli kontroller (DB). Minimal cikti (status metni).
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready")
        }).AllowAnonymous();

        // Detayli: tum kontroller + bilesen kirilimi. Yalniz admin rolu.
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = WriteDetailedAsync
        }).RequireAuthorization(policy => policy.RequireRole(IdentitySeeder.AdminRole));

        return app;
    }

    private static Task WriteDetailedAsync(HttpContext context, HealthReport report)
    {
        var entries = new JsonObject();
        foreach (var entry in report.Entries)
        {
            entries[entry.Key] = new JsonObject
            {
                ["status"] = entry.Value.Status.ToString(),
                ["description"] = entry.Value.Description,
                ["durationMs"] = Math.Round(entry.Value.Duration.TotalMilliseconds, 1),
                ["error"] = entry.Value.Exception?.Message
            };
        }

        var payload = new JsonObject
        {
            ["status"] = report.Status.ToString(),
            ["totalDurationMs"] = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            ["entries"] = entries
        };

        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(payload.ToJsonString(FlowJson.Relaxed));
    }
}
