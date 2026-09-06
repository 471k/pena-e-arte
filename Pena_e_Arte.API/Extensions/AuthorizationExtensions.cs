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
            .AddPolicy("AdminOnly", p => p.RequireRole("admin"));

        return services;
    }
}
