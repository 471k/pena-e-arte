using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Pena_e_Arte.API.Extensions;

public static class OpenTelemetryExtensions
{
    public static IServiceCollection AddApiOpenTelemetry(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        string? otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("Pena_e_Arte.API"))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation();
                if (!string.IsNullOrEmpty(otlpEndpoint))
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation();
                metrics.AddPrometheusExporter();
                if (!string.IsNullOrEmpty(otlpEndpoint))
                    metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            });

        return services;
    }
}
