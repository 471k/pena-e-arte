using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Pena_e_Arte.API.Endpoints;

namespace Pena_e_Arte.UnitTests.RateLimiting;

// Mirrors BillingEndpointsRateLimitingTests — mapping endpoints registers routing metadata only.
public class ContactEndpointsRateLimitingTests
{
    private static WebApplication BuildApp()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(Substitute.For<ISender>());
        WebApplication app = builder.Build();
        app.MapContactEndpoints();
        return app;
    }

    [Fact]
    public void ContactEndpoint_IsAnonymous_AndCarriesPublicWriteRateLimit()
    {
        using WebApplication app = BuildApp();

        RouteEndpoint endpoint = FindEndpoint(app, "POST", "/api/v1/contact");

        // Anonymous by design (public /contact page).
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull();

        // Rate-limited with the shared anonymous-write policy.
        EnableRateLimitingAttribute? rateLimit =
            endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>();
        rateLimit.Should().NotBeNull();
        rateLimit!.PolicyName.Should().Be("public-write");
    }

    private static RouteEndpoint FindEndpoint(WebApplication app, string method, string pattern) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(e =>
                e.RoutePattern.RawText == pattern &&
                (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) ?? false));
}
