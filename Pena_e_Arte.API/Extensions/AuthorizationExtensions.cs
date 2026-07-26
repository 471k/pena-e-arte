namespace Pena_e_Arte.API.Extensions;

public static class AuthorizationExtensions
{
    public static IServiceCollection AddApiAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy("ClientOnly", p => p.RequireRole("client"))
            .AddPolicy("ClientAndAbove", p => p.RequireRole("client", "artist", "owner", "issuer"))
            .AddPolicy("ArtistAndAbove", p => p.RequireRole("artist", "owner", "issuer"))
            .AddPolicy("OwnerOnly", p => p.RequireRole("owner", "issuer"))
            .AddPolicy("IssuerOnly", p => p.RequireRole("issuer"));

        return services;
    }
}
