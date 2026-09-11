using Pena_e_Arte.API.Authentication;

namespace Pena_e_Arte.API.Extensions;

public static class AuthorizationExtensions
{
    public static IServiceCollection AddApiAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy("ClientOnly", p => p.RequireRole("client"))
            .AddPolicy("ClientAndAbove", p => p.RequireRole("client", "artist", "owner", "admin"))
            .AddPolicy("ArtistAndAbove", p => p.RequireRole("artist", "owner", "admin"))
            .AddPolicy("OwnerOnly", p => p.RequireRole("owner", "admin"))
            .AddPolicy("AdminOnly", p => p.RequireRole("admin"))
            // Accepts ONLY the ApiKey scheme — a normal JWT-authenticated owner/admin never
            // satisfies this, and an API key never satisfies any role-based policy above,
            // since ApiKeyAuthenticationHandler issues no role claim at all.
            .AddPolicy("ExternalApiAccess", p => p
                .AddAuthenticationSchemes(ApiKeyAuthenticationSchemeOptions.Scheme)
                .RequireClaim(ApiKeyAuthenticationSchemeOptions.ScopeClaimType, ApiKeyAuthenticationSchemeOptions.ExternalApiScope));

        return services;
    }
}
