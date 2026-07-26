using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Pena_e_Arte.API.Extensions;

public static class CorsExtensions
{
    public static IServiceCollection AddApiCors(
        this IServiceCollection services,
        IConfiguration config,
        IHostEnvironment environment)
    {
        string[] allowedOrigins = config.GetSection("Cors:AllowedOrigins")
                                        .Get<string[]>() ?? [];

        // AllowAnyOrigin() is a deliberate local-dev convenience when nothing is configured —
        // keep it for every other environment. In Production specifically, an empty list would
        // otherwise silently open CORS to every origin with no warning; fail fast instead,
        // matching this remediation's own precedent (Phase 5's JWT signing-key guard) of
        // refusing to start on a missing security-critical value rather than degrading quietly.
        if (allowedOrigins.Length == 0 && environment.IsProduction())
        {
            throw new InvalidOperationException(
                "Cors:AllowedOrigins is empty in Production — refusing to start with CORS open " +
                "to every origin. Set Cors__AllowedOrigins__0 (and further indices as needed).");
        }

        services.AddCors(opt =>
            opt.AddDefaultPolicy(p =>
            {
                if (allowedOrigins.Length == 0)
                    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
                else
                    p.WithOrigins(allowedOrigins)
                     .AllowAnyHeader()
                     .AllowAnyMethod()
                     .AllowCredentials();
            }));

        return services;
    }
}
