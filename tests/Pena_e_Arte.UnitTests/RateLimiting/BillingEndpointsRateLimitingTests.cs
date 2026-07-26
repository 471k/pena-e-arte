using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Pena_e_Arte.API.Endpoints;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.RateLimiting;

// Mirrors AuthEndpointsRateLimitingTests' technique — mapping endpoints registers routing
// metadata only, so this needs no DI container beyond the services the route handlers'
// parameters require to be resolvable, no MediatR handler execution, DB, or Redis.
public class BillingEndpointsRateLimitingTests
{
    private static WebApplication BuildApp()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(Substitute.For<ISender>());
        builder.Services.AddSingleton(Substitute.For<ICurrentTenant>());
        WebApplication app = builder.Build();
        app.MapPaymentEndpoints();
        app.MapBillingEndpoints();
        return app;
    }

    [Theory]
    [InlineData("POST", "/api/v1/payments/")]
    [InlineData("POST", "/api/v1/payments/deposit")]
    [InlineData("POST", "/api/v1/payments/{id:guid}/capture")]
    [InlineData("POST", "/api/v1/payments/{id:guid}/refund")]
    [InlineData("POST", "/api/v1/billing/subscription/checkout")]
    [InlineData("POST", "/api/v1/billing/subscription/checkout/finalize")]
    public void StripeCallingEndpoint_CarriesBillingRateLimitingPolicy(string method, string pattern)
    {
        using WebApplication app = BuildApp();

        RouteEndpoint endpoint = FindEndpoint(app, method, pattern);

        EnableRateLimitingAttribute? attribute =
            endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>();
        attribute.Should().NotBeNull();
        attribute!.PolicyName.Should().Be("billing");
    }

    [Theory]
    [InlineData("POST", "/api/v1/payments/cash")]
    [InlineData("POST", "/api/v1/payments/{id:guid}/cash/confirm")]
    [InlineData("GET", "/api/v1/payments/")]
    [InlineData("GET", "/api/v1/payments/appointment/{appointmentId:guid}")]
    [InlineData("GET", "/api/v1/payments/{id:guid}/client-secret")]
    [InlineData("GET", "/api/v1/payments/{id:guid}/invoice")]
    public void NonStripeCallingOrReadEndpoint_DoesNotCarryBillingRateLimitingPolicy(
        string method, string pattern)
    {
        using WebApplication app = BuildApp();

        RouteEndpoint endpoint = FindEndpoint(app, method, pattern);

        endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>().Should().BeNull();
    }

    private static RouteEndpoint FindEndpoint(WebApplication app, string method, string pattern) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(e =>
                e.RoutePattern.RawText == pattern &&
                (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) ?? false));
}
