using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Pena_e_Arte.API.Middleware;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Middleware;

/// <summary>
/// TenantMiddleware's impersonation gate — the actual, sole enforcement mechanism for
/// Support Impersonation scope (see AuthorizationExtensions.cs: every RBAC policy already
/// grants "admin" every role-based permission, so without this gate an impersonation token
/// would already reach every endpoint). Uses a real FakeDbContext (InMemory EF) rather than
/// a substitute, since the gate runs a real query against ImpersonationSessions.
/// </summary>
public class ImpersonationGateTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly ISubscriptionAccessService _subscriptions = Substitute.For<ISubscriptionAccessService>();
    private readonly Guid _studioId = Guid.NewGuid();

    private TenantMiddleware CreateSut(RequestDelegate next) => new(next);

    [Fact]
    public async Task InvokeAsync_NoImpClaim_NeverTouchesImpersonationSessions()
    {
        // "admin" role (not "owner") — subscription enforcement is separately tested in
        // TenantMiddlewareTests.cs; this test only asserts the impersonation gate itself is
        // a true no-op with no "imp" claim, so an unrelated subscription-substitute default
        // (NSubstitute returns false/null unless configured) must not interfere.
        DefaultHttpContext context = ContextWithClaims(_studioId, role: "admin");

        bool nextCalled = false;
        await CreateSut(_ => { nextCalled = true; return Task.CompletedTask; })
            .InvokeAsync(context, _tenant, _subscriptions, _db);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ImpClaim_AllowListedRoute_ActiveSession_CallsNext()
    {
        Guid sessionId = SeedActiveSession(_studioId);
        DefaultHttpContext context = ContextWithClaims(_studioId, sessionId, "/api/v1/appointments", "GET");

        bool nextCalled = false;
        await CreateSut(_ => { nextCalled = true; return Task.CompletedTask; })
            .InvokeAsync(context, _tenant, _subscriptions, _db);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ImpClaim_RouteNotOnAllowList_ThrowsImpersonationScopeException()
    {
        Guid sessionId = SeedActiveSession(_studioId);
        DefaultHttpContext context = ContextWithClaims(_studioId, sessionId, "/api/v1/payments", "GET");

        Func<Task> act = () => CreateSut(_ => Task.CompletedTask)
            .InvokeAsync(context, _tenant, _subscriptions, _db);

        await act.Should().ThrowAsync<ImpersonationScopeException>();
    }

    [Fact]
    public async Task InvokeAsync_ImpClaim_WriteToAllowListedResource_ThrowsImpersonationScopeException()
    {
        Guid sessionId = SeedActiveSession(_studioId);
        DefaultHttpContext context = ContextWithClaims(_studioId, sessionId, "/api/v1/appointments", "POST");

        Func<Task> act = () => CreateSut(_ => Task.CompletedTask)
            .InvokeAsync(context, _tenant, _subscriptions, _db);

        await act.Should().ThrowAsync<ImpersonationScopeException>();
    }

    [Fact]
    public async Task InvokeAsync_ImpClaim_EndedSession_ThrowsImpersonationScopeException()
    {
        Guid sessionId = SeedSession(_studioId, endedAt: DateTime.UtcNow.AddMinutes(-1), expiresAt: DateTime.UtcNow.AddMinutes(30));
        DefaultHttpContext context = ContextWithClaims(_studioId, sessionId, "/api/v1/appointments", "GET");

        Func<Task> act = () => CreateSut(_ => Task.CompletedTask)
            .InvokeAsync(context, _tenant, _subscriptions, _db);

        await act.Should().ThrowAsync<ImpersonationScopeException>();
    }

    [Fact]
    public async Task InvokeAsync_ImpClaim_ExpiredSession_ThrowsImpersonationScopeException()
    {
        Guid sessionId = SeedSession(_studioId, endedAt: null, expiresAt: DateTime.UtcNow.AddMinutes(-1));
        DefaultHttpContext context = ContextWithClaims(_studioId, sessionId, "/api/v1/appointments", "GET");

        Func<Task> act = () => CreateSut(_ => Task.CompletedTask)
            .InvokeAsync(context, _tenant, _subscriptions, _db);

        await act.Should().ThrowAsync<ImpersonationScopeException>();
    }

    [Fact]
    public async Task InvokeAsync_ImpClaim_UnknownSessionId_ThrowsImpersonationScopeException()
    {
        DefaultHttpContext context = ContextWithClaims(_studioId, Guid.NewGuid(), "/api/v1/appointments", "GET");

        Func<Task> act = () => CreateSut(_ => Task.CompletedTask)
            .InvokeAsync(context, _tenant, _subscriptions, _db);

        await act.Should().ThrowAsync<ImpersonationScopeException>();
    }

    [Fact]
    public async Task InvokeAsync_ImpClaim_MalformedSessionId_ThrowsImpersonationScopeException()
    {
        DefaultHttpContext context = new();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("tenant_id", _studioId.ToString()), new Claim("imp", "not-a-guid")], "test"));
        context.Request.Path = "/api/v1/appointments";
        context.Request.Method = "GET";

        Func<Task> act = () => CreateSut(_ => Task.CompletedTask)
            .InvokeAsync(context, _tenant, _subscriptions, _db);

        await act.Should().ThrowAsync<ImpersonationScopeException>();
    }

    private Guid SeedActiveSession(Guid studioId) =>
        SeedSession(studioId, endedAt: null, expiresAt: DateTime.UtcNow.AddMinutes(30));

    private Guid SeedSession(Guid studioId, DateTime? endedAt, DateTime expiresAt)
    {
        ImpersonationSession session = ImpersonationSession.Start(
            Guid.NewGuid(), studioId, "reason", TimeSpan.FromMinutes(45));
        _db.ImpersonationSessions.Add(session);
        _db.Entry(session).Property(s => s.ExpiresAt).CurrentValue = expiresAt;
        _db.Entry(session).Property(s => s.EndedAt).CurrentValue = endedAt;
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return session.Id;
    }

    private static DefaultHttpContext ContextWithClaims(
        Guid studioId, string role, string path = "/api/v1/appointments", string method = "GET")
    {
        DefaultHttpContext context = new();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("tenant_id", studioId.ToString()), new Claim(ClaimTypes.Role, role)], "test"));
        context.Request.Path = path;
        context.Request.Method = method;
        return context;
    }

    private static DefaultHttpContext ContextWithClaims(
        Guid studioId, Guid sessionId, string path, string method)
    {
        DefaultHttpContext context = new();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("tenant_id", studioId.ToString()),
                new Claim(ClaimTypes.Role, "admin"),
                new Claim("imp", sessionId.ToString()),
            ], "test"));
        context.Request.Path = path;
        context.Request.Method = method;
        return context;
    }
}
