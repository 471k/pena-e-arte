using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Pena_e_Arte.API.Extensions;

public static class CorsExtensions
{
    public static IServiceCollection AddApiCors(
        this IServiceCollection services,
        IConfiguration config)
    {
        string[] allowedOrigins = config.GetSection("Cors:AllowedOrigins")
                                        .Get<string[]>() ?? [];

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
