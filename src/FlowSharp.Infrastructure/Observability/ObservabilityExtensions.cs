using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using FlowSharp.Application.Diagnostics;

namespace FlowSharp.Infrastructure.Observability;

/// <summary>
/// OpenTelemetry kurulumu (traces + metrics). appsettings "OpenTelemetry" bolumuyle yonetilir:
/// <c>Enabled</c> kapaliyken hicbir sey kaydedilmez; <c>OtlpEndpoint</c> tanimliysa OTLP'ye,
/// tanimsizsa konsola aktarir. Web ve Worker farkli <paramref name="serviceName"/> ile cagirir.
/// </summary>
public static class ObservabilityExtensions
{
    public static IServiceCollection AddObservability(
        this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        var section = configuration.GetSection("OpenTelemetry");
        if (!section.GetValue("Enabled", false))
        {
            return services;
        }

        var otlpEndpoint = section.GetValue<string>("OtlpEndpoint");
        var hasOtlp = !string.IsNullOrWhiteSpace(otlpEndpoint);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(FlowSharpTelemetry.ActivitySourceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSource("Npgsql"); // Postgres surucusu kendi activity'lerini bu kaynakla yayar.

                if (hasOtlp)
                {
                    tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint!));
                }
                else
                {
                    tracing.AddConsoleExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(FlowSharpTelemetry.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (hasOtlp)
                {
                    metrics.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint!));
                }
                else
                {
                    metrics.AddConsoleExporter();
                }
            });

        return services;
    }
}
