using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Pena_e_Arte.API.Extensions;

public static class OpenTelemetryExtensions
{
    public static IServiceCollection AddApiOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string? otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("Pena_e_Arte.API"))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation();
                // Redis commands as child spans, with timing events (enqueued / sent / response
                // received). Statements stay non-verbose: the command name only, never the key
                // (rate-limiter keys hold the client IP).
                tracing.AddRedisInstrumentation(o => o.SetVerboseDatabaseStatements = false);
                if (!string.IsNullOrEmpty(otlpEndpoint))
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation();
                // .NET's built-in runtime meter: thread pool (threads, queue length), lock
                // contention, GC, CPU time — what's needed to see a starved thread pool.
                metrics.AddMeter("System.Runtime");
                metrics.AddPrometheusExporter();
                if (!string.IsNullOrEmpty(otlpEndpoint))
                    metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            });

        return services;
    }
}
