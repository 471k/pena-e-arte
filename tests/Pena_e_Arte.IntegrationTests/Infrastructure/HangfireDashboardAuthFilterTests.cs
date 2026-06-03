using System.Security.Claims;
using FluentAssertions;
using Hangfire;
using Hangfire.AspNetCore;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Pena_e_Arte.API.Extensions;

namespace Pena_e_Arte.IntegrationTests.Infrastructure;

public class HangfireDashboardAuthFilterTests
{
    private readonly HangfireDashboardAuthFilter _sut = new();

    [Fact]
    public void Authorize_AuthenticatedIssuer_ReturnsTrue()
    {
        DashboardContext ctx = DashboardContextFor(authenticated: true, role: "issuer");

        bool result = _sut.Authorize(ctx);

        result.Should().BeTrue();
    }

    [Fact]
    public void Authorize_AuthenticatedOwner_ReturnsFalse()
    {
        DashboardContext ctx = DashboardContextFor(authenticated: true, role: "owner");

        bool result = _sut.Authorize(ctx);

        result.Should().BeFalse();
    }

    [Fact]
    public void Authorize_AuthenticatedArtist_ReturnsFalse()
    {
        DashboardContext ctx = DashboardContextFor(authenticated: true, role: "artist");

        bool result = _sut.Authorize(ctx);

        result.Should().BeFalse();
    }

    [Fact]
    public void Authorize_UnauthenticatedUser_ReturnsFalse()
    {
        DashboardContext ctx = DashboardContextFor(authenticated: false, role: null);

        bool result = _sut.Authorize(ctx);

        result.Should().BeFalse();
    }

    [Fact]
    public void Authorize_UnauthenticatedWithIssuerRole_ReturnsFalse()
    {
        DefaultHttpContext httpContext = BuildHttpContext(authenticated: false, role: "issuer");

        DashboardContext ctx = new AspNetCoreDashboardContext(
            Substitute.For<JobStorage>(), new DashboardOptions(), httpContext);

        bool result = _sut.Authorize(ctx);

        result.Should().BeFalse();
    }

    [Fact]
    public void Authorize_AuthenticatedWithNoRole_ReturnsFalse()
    {
        DashboardContext ctx = DashboardContextFor(authenticated: true, role: null);

        bool result = _sut.Authorize(ctx);

        result.Should().BeFalse();
    }

    private static DashboardContext DashboardContextFor(bool authenticated, string? role)
    {
        DefaultHttpContext httpContext = BuildHttpContext(authenticated, role);
        return new AspNetCoreDashboardContext(
            Substitute.For<JobStorage>(), new DashboardOptions(), httpContext);
    }

    private static DefaultHttpContext BuildHttpContext(bool authenticated, string? role)
    {
        DefaultHttpContext httpContext = new();
        httpContext.RequestServices = new ServiceCollection().BuildServiceProvider();

        List<Claim> claims = [];
        if (role is not null) claims.Add(new Claim(ClaimTypes.Role, role));

        string? authType = authenticated ? "test" : null;
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authType));
        return httpContext;
    }
}
