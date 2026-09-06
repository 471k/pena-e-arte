using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Hangfire;
using Hangfire.AspNetCore;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Pena_e_Arte.API.Extensions;

namespace Pena_e_Arte.IntegrationTests.Infrastructure;

public class HangfireDashboardAuthFilterTests
{
    private const string DashboardUsername = "test-dashboard-user";
    private const string DashboardPassword = "test-dashboard-password-1234";

    [Fact]
    public void Authorize_AuthenticatedAdmin_ReturnsTrue()
    {
        DashboardContext ctx = DashboardContextFor(authenticated: true, role: "admin");

        bool result = BuildSut().Authorize(ctx);

        result.Should().BeTrue();
    }

    [Fact]
    public void Authorize_AuthenticatedOwner_ReturnsFalse()
    {
        DashboardContext ctx = DashboardContextFor(authenticated: true, role: "owner");

        bool result = BuildSut().Authorize(ctx);

        result.Should().BeFalse();
    }

    [Fact]
    public void Authorize_AuthenticatedArtist_ReturnsFalse()
    {
        DashboardContext ctx = DashboardContextFor(authenticated: true, role: "artist");

        bool result = BuildSut().Authorize(ctx);

        result.Should().BeFalse();
    }

    [Fact]
    public void Authorize_UnauthenticatedUser_ReturnsFalse()
    {
        DashboardContext ctx = DashboardContextFor(authenticated: false, role: null);

        bool result = BuildSut().Authorize(ctx);

        result.Should().BeFalse();
    }

    [Fact]
    public void Authorize_UnauthenticatedWithAdminRole_ReturnsFalse()
    {
        DefaultHttpContext httpContext = BuildHttpContext(authenticated: false, role: "admin");

        DashboardContext ctx = new AspNetCoreDashboardContext(
            Substitute.For<JobStorage>(), new DashboardOptions(), httpContext);

        bool result = BuildSut().Authorize(ctx);

        result.Should().BeFalse();
    }

    [Fact]
    public void Authorize_AuthenticatedWithNoRole_ReturnsFalse()
    {
        DashboardContext ctx = DashboardContextFor(authenticated: true, role: null);

        bool result = BuildSut().Authorize(ctx);

        result.Should().BeFalse();
    }

    // /hangfire is reached via plain browser navigation, which never carries the SPA's JWT
    // (localStorage/sessionStorage only, attached to fetch/XHR by baseQuery.ts — never sent on a
    // top-level navigation, and there is no cookie-auth scheme registered). Basic Auth is the
    // real access mechanism for that path; the admin-JWT check above remains an additional layer.
    [Fact]
    public void Authorize_CorrectBasicCredentials_ReturnsTrue()
    {
        DefaultHttpContext httpContext = BuildHttpContext(authenticated: false, role: null);
        httpContext.Request.Headers.Authorization = BasicHeader(DashboardUsername, DashboardPassword);
        DashboardContext ctx = new AspNetCoreDashboardContext(
            Substitute.For<JobStorage>(), new DashboardOptions(), httpContext);

        bool result = BuildSut().Authorize(ctx);

        result.Should().BeTrue();
    }

    [Fact]
    public void Authorize_IncorrectBasicPassword_ReturnsFalseAndChallenges()
    {
        DefaultHttpContext httpContext = BuildHttpContext(authenticated: false, role: null);
        httpContext.Request.Headers.Authorization = BasicHeader(DashboardUsername, "wrong-password");
        DashboardContext ctx = new AspNetCoreDashboardContext(
            Substitute.For<JobStorage>(), new DashboardOptions(), httpContext);

        bool result = BuildSut().Authorize(ctx);

        result.Should().BeFalse();
        httpContext.Response.Headers.WWWAuthenticate.ToString().Should().Contain("Basic");
    }

    [Fact]
    public void Authorize_NoAuthorizationHeaderAtAll_ReturnsFalseAndChallengesBasicAuth()
    {
        DefaultHttpContext httpContext = BuildHttpContext(authenticated: false, role: null);
        DashboardContext ctx = new AspNetCoreDashboardContext(
            Substitute.For<JobStorage>(), new DashboardOptions(), httpContext);

        bool result = BuildSut().Authorize(ctx);

        result.Should().BeFalse();
        httpContext.Response.Headers.WWWAuthenticate.ToString().Should().Contain("Basic");
    }

    [Fact]
    public void Authorize_MalformedAuthorizationHeader_ReturnsFalseWithoutThrowing()
    {
        DefaultHttpContext httpContext = BuildHttpContext(authenticated: false, role: null);
        httpContext.Request.Headers.Authorization = "Basic not-valid-base64!!!";
        DashboardContext ctx = new AspNetCoreDashboardContext(
            Substitute.For<JobStorage>(), new DashboardOptions(), httpContext);

        bool result = BuildSut().Authorize(ctx);

        result.Should().BeFalse();
    }

    [Fact]
    public void Authorize_DashboardCredentialsNotConfigured_BasicAuthAlwaysFails()
    {
        DefaultHttpContext httpContext = BuildHttpContext(authenticated: false, role: null);
        httpContext.Request.Headers.Authorization = BasicHeader("anything", "anything");
        DashboardContext ctx = new AspNetCoreDashboardContext(
            Substitute.For<JobStorage>(), new DashboardOptions(), httpContext);

        bool result = BuildSut(username: null, password: null).Authorize(ctx);

        result.Should().BeFalse();
    }

    private static HangfireDashboardAuthFilter BuildSut(
        string? username = DashboardUsername, string? password = DashboardPassword)
    {
        Dictionary<string, string?> values = [];
        if (username is not null) values["Hangfire:DashboardUsername"] = username;
        if (password is not null) values["Hangfire:DashboardPassword"] = password;

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new HangfireDashboardAuthFilter(configuration);
    }

    private static string BasicHeader(string username, string password) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));

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
