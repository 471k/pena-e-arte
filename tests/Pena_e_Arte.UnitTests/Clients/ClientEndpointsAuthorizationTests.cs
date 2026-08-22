using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Pena_e_Arte.API.Endpoints;

namespace Pena_e_Arte.UnitTests.Clients;

// Mirrors ContactEndpointsRateLimitingTests — mapping endpoints registers routing metadata only,
// so a real HTTP host/JWT isn't needed to assert which authorization policy a route carries.
// Covers the PATCH .../artist route added for client-artist assignment: it must require
// OwnerOnly, not the ArtistAndAbove policy the rest of the /clients group uses.
// For real end-to-end enforcement through the ASP.NET Core authorization pipeline (not just
// route metadata), see ClientArtistEndpointAuthorizationTests in the IntegrationTests project.
public class ClientEndpointsAuthorizationTests
{
    private static WebApplication BuildApp()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(Substitute.For<ISender>());
        builder.Services.AddAuthorization();
        WebApplication app = builder.Build();
        app.MapClientEndpoints();
        return app;
    }

    [Fact]
    public void UpdateClientArtistEndpoint_RequiresOwnerOnlyPolicy()
    {
        using WebApplication app = BuildApp();

        RouteEndpoint endpoint = FindEndpoint(app, "PATCH", "/api/v1/clients/{clientId:guid}/artist");

        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Should().ContainSingle(a => a.Policy == "OwnerOnly");
    }

    [Fact]
    public void CreateClientEndpoint_RequiresArtistAndAbovePolicy()
    {
        using WebApplication app = BuildApp();

        RouteEndpoint endpoint = FindEndpoint(app, "POST", "/api/v1/clients/");

        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Should().ContainSingle(a => a.Policy == "ArtistAndAbove");
    }

    private static RouteEndpoint FindEndpoint(WebApplication app, string method, string pattern) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(e =>
                e.RoutePattern.RawText == pattern &&
                (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) ?? false));
}
