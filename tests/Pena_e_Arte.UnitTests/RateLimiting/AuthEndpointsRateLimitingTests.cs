using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Pena_e_Arte.API.Endpoints;

namespace Pena_e_Arte.UnitTests.RateLimiting;

// Mapping endpoints only registers routing metadata — no handler is ever invoked, so this
// needs no DI container, MediatR, database, or Redis. It proves the "auth" rate-limiting
// policy is actually wired to these three routes (Finding 3), the same way the existing
// login/register/oauth/forgot-password endpoints already carry it.
public class AuthEndpointsRateLimitingTests
{
    private static WebApplication BuildApp()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(Substitute.For<ISender>());
        WebApplication app = builder.Build();
        app.MapAuthEndpoints();
        return app;
    }

    [Theory]
    [InlineData("POST", "/api/v1/auth/reset-password")]
    [InlineData("POST", "/api/v1/auth/refresh")]
    [InlineData("GET", "/api/v1/auth/verify-email")]
    public void Endpoint_CarriesAuthRateLimitingPolicy(string method, string pattern)
    {
        using WebApplication app = BuildApp();

        RouteEndpoint endpoint = FindEndpoint(app, method, pattern);

        EnableRateLimitingAttribute? attribute =
            endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>();
        attribute.Should().NotBeNull();
        attribute!.PolicyName.Should().Be("auth");
    }

    private static RouteEndpoint FindEndpoint(WebApplication app, string method, string pattern) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(e =>
                e.RoutePattern.RawText == pattern &&
                (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) ?? false));
}
