using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Pena_e_Arte.API.Endpoints;

namespace Pena_e_Arte.UnitTests.Reports;

// Mirrors ClientEndpointsAuthorizationTests — mapping endpoints registers routing metadata only,
// so a real HTTP host/JWT isn't needed to assert which authorization policy a route carries.
public class ReportEndpointsAuthorizationTests
{
    private static WebApplication BuildApp()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(Substitute.For<ISender>());
        builder.Services.AddAuthorization();
        WebApplication app = builder.Build();
        app.MapReportEndpoints();
        return app;
    }

    [Fact]
    public void ExportRevenueCsvEndpoint_RequiresOwnerOnlyPolicy()
    {
        using WebApplication app = BuildApp();

        RouteEndpoint endpoint = FindEndpoint(app, "GET", "/api/v1/reports/revenue/export.csv");

        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Should().ContainSingle(a => a.Policy == "OwnerOnly");
    }

    private static RouteEndpoint FindEndpoint(WebApplication app, string method, string pattern) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(e =>
                e.RoutePattern.RawText == pattern &&
                (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) ?? false));
}
