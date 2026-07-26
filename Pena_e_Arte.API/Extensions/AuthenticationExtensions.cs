using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Pena_e_Arte.API.Extensions;

public static class AuthenticationExtensions
{
    // The only backstop against a missing/weak signing key was previously docker-compose.yml's
    // ${JWT_SECRET_KEY:?...} — a deployment-file-level control. Any path that doesn't go through
    // that specific compose file (a hand-written K3s manifest, dotnet run with a stray
    // environment) started up successfully with an empty key and no warning. This guard makes
    // the application itself refuse to start, independent of how it's deployed.
    private const int MinimumSecretKeyBytes = 32;

    public static IServiceCollection AddApiAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string? secretKey = configuration["Jwt:SecretKey"];
        if (string.IsNullOrEmpty(secretKey) || Encoding.UTF8.GetByteCount(secretKey) < MinimumSecretKeyBytes)
        {
            throw new InvalidOperationException(
                $"Jwt:SecretKey must be set and at least {MinimumSecretKeyBytes} bytes " +
                $"({MinimumSecretKeyBytes * 8} bits) — refusing to start with a missing or weak " +
                "JWT signing key. Set JWT_SECRET_KEY in your environment.");
        }

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = configuration["Jwt:Issuer"],
                    ValidAudience = configuration["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                    ClockSkew = TimeSpan.Zero
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        string? accessToken = ctx.Request.Query["access_token"];
                        PathString path = ctx.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                            ctx.Token = accessToken;
                        return Task.CompletedTask;
                    }
                };
            });

        return services;
    }
}
