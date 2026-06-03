using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Pena_e_Arte.API.Middleware;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.IntegrationTests.Middleware;

public class TenantMiddlewareTests
{
    private readonly ICurrentTenant             _tenant        = Substitute.For<ICurrentTenant>();
    private readonly ISubscriptionAccessService _subscriptions = Substitute.For<ISubscriptionAccessService>();
    private readonly Guid                       _studioId      = Guid.NewGuid();

    public TenantMiddlewareTests() =>
        _subscriptions
            .GetSnapshotAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SubscriptionSnapshot?>(
                new SubscriptionSnapshot(SubscriptionStatus.Active, DateTime.UtcNow.AddDays(14), DateTime.UtcNow.AddDays(21))));

    private TenantMiddleware CreateSut(RequestDelegate next) => new(next);

    // ------------------------------------------------------------------
    // Tenant identification
    // ------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_ValidTenantIdClaim_CallsSetTenantWithCorrectGuid()
    {
        DefaultHttpContext context = ContextWithTenant(_studioId);

        await CreateSut(_ => Task.CompletedTask).InvokeAsync(context, _tenant, _subscriptions);

        _tenant.Received(1).SetTenant(_studioId);
    }

    [Fact]
    public async Task InvokeAsync_NoTenantIdClaim_DoesNotCallSetTenant()
    {
        DefaultHttpContext context = new();

        await CreateSut(_ => Task.CompletedTask).InvokeAsync(context, _tenant, _subscriptions);

        _tenant.DidNotReceive().SetTenant(Arg.Any<Guid>());
    }

    [Fact]
    public async Task InvokeAsync_InvalidTenantIdClaim_DoesNotCallSetTenant()
    {
        DefaultHttpContext context = ContextWithClaim("tenant_id", "not-a-guid");

        await CreateSut(_ => Task.CompletedTask).InvokeAsync(context, _tenant, _subscriptions);

        _tenant.DidNotReceive().SetTenant(Arg.Any<Guid>());
    }

    [Fact]
    public async Task InvokeAsync_EmptyTenantIdClaim_DoesNotCallSetTenant()
    {
        DefaultHttpContext context = ContextWithClaim("tenant_id", "");

        await CreateSut(_ => Task.CompletedTask).InvokeAsync(context, _tenant, _subscriptions);

        _tenant.DidNotReceive().SetTenant(Arg.Any<Guid>());
    }

    [Fact]
    public async Task InvokeAsync_ValidTenantId_StillCallsNext()
    {
        DefaultHttpContext context = ContextWithTenant(_studioId);
        bool nextCalled = false;

        await CreateSut(_ => { nextCalled = true; return Task.CompletedTask; })
            .InvokeAsync(context, _tenant, _subscriptions);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_NoClaim_StillCallsNext()
    {
        DefaultHttpContext context = new();
        bool nextCalled = false;

        await CreateSut(_ => { nextCalled = true; return Task.CompletedTask; })
            .InvokeAsync(context, _tenant, _subscriptions);

        nextCalled.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // Full-access scenarios
    // ------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_ActiveSubscription_AllowsWriteRequest()
    {
        SetupSnapshot(SubscriptionStatus.Active, DateTime.UtcNow.AddDays(-14), DateTime.UtcNow.AddDays(-7));
        DefaultHttpContext context = ContextWithTenant(_studioId, "/api/v1/appointments", "POST");

        Func<Task> act = () => CreateSut(_ => Task.CompletedTask)
            .InvokeAsync(context, _tenant, _subscriptions);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InvokeAsync_TrialNotExpired_AllowsWriteRequest()
    {
        SetupSnapshot(SubscriptionStatus.Trialing, DateTime.UtcNow.AddDays(10), DateTime.UtcNow.AddDays(17));
        DefaultHttpContext context = ContextWithTenant(_studioId, "/api/v1/appointments", "POST");

        Func<Task> act = () => CreateSut(_ => Task.CompletedTask)
            .InvokeAsync(context, _tenant, _subscriptions);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InvokeAsync_NoSubscriptionRecord_AllowsRequest()
    {
        _subscriptions
            .GetSnapshotAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SubscriptionSnapshot?>(null));
        DefaultHttpContext context = ContextWithTenant(_studioId, "/api/v1/appointments", "POST");

        Func<Task> act = () => CreateSut(_ => Task.CompletedTask)
            .InvokeAsync(context, _tenant, _subscriptions);

        await act.Should().NotThrowAsync();
    }

    // ------------------------------------------------------------------
    // Grace period — read-only
    // ------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_GracePeriod_GetRequest_AllowsRequest()
    {
        SetupSnapshot(SubscriptionStatus.GracePeriod, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(6));
        DefaultHttpContext context = ContextWithTenant(_studioId, "/api/v1/clients", "GET");

        Func<Task> act = () => CreateSut(_ => Task.CompletedTask)
            .InvokeAsync(context, _tenant, _subscriptions);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InvokeAsync_GracePeriod_PostRequest_ThrowsSubscriptionRequiredException()
    {
        SetupSnapshot(SubscriptionStatus.GracePeriod, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(6));
        DefaultHttpContext context = ContextWithTenant(_studioId, "/api/v1/appointments", "POST");

        Func<Task> act = () => CreateSut(_ => Task.CompletedTask)
            .InvokeAsync(context, _tenant, _subscriptions);

        await act.Should().ThrowAsync<SubscriptionRequiredException>()
            .WithMessage("*grace period*");
    }

    [Fact]
    public async Task InvokeAsync_GracePeriod_PutRequest_ThrowsSubscriptionRequiredException()
    {
        SetupSnapshot(SubscriptionStatus.GracePeriod, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(6));
        DefaultHttpContext context = ContextWithTenant(_studioId, "/api/v1/clients/1", "PUT");

        Func<Task> act = () => CreateSut(_ => Task.CompletedTask)
            .InvokeAsync(context, _tenant, _subscriptions);

        await act.Should().ThrowAsync<SubscriptionRequiredException>();
    }

    [Fact]
    public async Task InvokeAsync_GracePeriod_DeleteRequest_ThrowsSubscriptionRequiredException()
    {
        SetupSnapshot(SubscriptionStatus.GracePeriod, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(6));
        DefaultHttpContext context = ContextWithTenant(_studioId, "/api/v1/appointments/1", "DELETE");

        Func<Task> act = () => CreateSut(_ => Task.CompletedTask)
            .InvokeAsync(context, _tenant, _subscriptions);

        await act.Should().ThrowAsync<SubscriptionRequiredException>();
    }

    // ------------------------------------------------------------------
    // Blocked scenarios (rules 4, 5, 6)
    // ------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_CancelledSubscription_BlocksReadRequest()
    {
        SetupSnapshot(SubscriptionStatus.Cancelled, DateTime.UtcNow.AddDays(-21), DateTime.UtcNow.AddDays(-7));
        DefaultHttpContext context = ContextWithTenant(_studioId, "/api/v1/appointments", "GET");

        Func<Task> act = () => CreateSut(_ => Task.CompletedTask)
            .InvokeAsync(context, _tenant, _subscriptions);

        await act.Should().ThrowAsync<SubscriptionRequiredException>()
            .WithMessage("*expired*");
    }

    [Fact]
    public async Task InvokeAsync_CancelledSubscription_BlocksWriteRequest()
    {
        SetupSnapshot(SubscriptionStatus.Cancelled, DateTime.UtcNow.AddDays(-21), DateTime.UtcNow.AddDays(-7));
        DefaultHttpContext context = ContextWithTenant(_studioId, "/api/v1/appointments", "POST");

        Func<Task> act = () => CreateSut(_ => Task.CompletedTask)
            .InvokeAsync(context, _tenant, _subscriptions);

        await act.Should().ThrowAsync<SubscriptionRequiredException>();
    }

    [Fact]
    public async Task InvokeAsync_PastDueSubscription_BlocksAllRequests()
    {
        SetupSnapshot(SubscriptionStatus.PastDue, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(-23));
        DefaultHttpContext context = ContextWithTenant(_studioId, "/api/v1/appointments", "GET");

        Func<Task> act = () => CreateSut(_ => Task.CompletedTask)
            .InvokeAsync(context, _tenant, _subscriptions);

        await act.Should().ThrowAsync<SubscriptionRequiredException>()
            .WithMessage("*overdue*");
    }

    [Fact]
    public async Task InvokeAsync_GracePeriodExpired_BlocksReadRequest()
    {
        // Grace period job hasn't run yet, but the dates show it should be suspended
        SetupSnapshot(SubscriptionStatus.GracePeriod, DateTime.UtcNow.AddDays(-21), DateTime.UtcNow.AddDays(-7));
        DefaultHttpContext context = ContextWithTenant(_studioId, "/api/v1/appointments", "GET");

        Func<Task> act = () => CreateSut(_ => Task.CompletedTask)
            .InvokeAsync(context, _tenant, _subscriptions);

        await act.Should().ThrowAsync<SubscriptionRequiredException>()
            .WithMessage("*expired*");
    }

    // ------------------------------------------------------------------
    // Bypass scenarios
    // ------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_IssuerRole_BypassesEnforcement()
    {
        SetupSnapshot(SubscriptionStatus.Cancelled, DateTime.UtcNow.AddDays(-21), DateTime.UtcNow.AddDays(-14));
        DefaultHttpContext context = ContextWithTenantAndRole(_studioId, "issuer", "/api/v1/appointments", "POST");

        Func<Task> act = () => CreateSut(_ => Task.CompletedTask)
            .InvokeAsync(context, _tenant, _subscriptions);

        await act.Should().NotThrowAsync();
        await _subscriptions.DidNotReceive()
            .GetSnapshotAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_BillingPath_BypassesEnforcement()
    {
        SetupSnapshot(SubscriptionStatus.Cancelled, DateTime.UtcNow.AddDays(-21), DateTime.UtcNow.AddDays(-14));
        DefaultHttpContext context = ContextWithTenant(_studioId, "/api/v1/billing/subscription", "POST");

        Func<Task> act = () => CreateSut(_ => Task.CompletedTask)
            .InvokeAsync(context, _tenant, _subscriptions);

        await act.Should().NotThrowAsync();
        await _subscriptions.DidNotReceive()
            .GetSnapshotAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_AuthPath_BypassesEnforcement()
    {
        SetupSnapshot(SubscriptionStatus.Cancelled, DateTime.UtcNow.AddDays(-21), DateTime.UtcNow.AddDays(-14));
        DefaultHttpContext context = ContextWithTenant(_studioId, "/api/v1/auth/login", "POST");

        Func<Task> act = () => CreateSut(_ => Task.CompletedTask)
            .InvokeAsync(context, _tenant, _subscriptions);

        await act.Should().NotThrowAsync();
        await _subscriptions.DidNotReceive()
            .GetSnapshotAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_WebhookPath_BypassesEnforcement()
    {
        SetupSnapshot(SubscriptionStatus.Cancelled, DateTime.UtcNow.AddDays(-21), DateTime.UtcNow.AddDays(-14));
        DefaultHttpContext context = ContextWithTenant(_studioId, "/api/v1/webhooks/stripe/billing", "POST");

        Func<Task> act = () => CreateSut(_ => Task.CompletedTask)
            .InvokeAsync(context, _tenant, _subscriptions);

        await act.Should().NotThrowAsync();
        await _subscriptions.DidNotReceive()
            .GetSnapshotAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_HealthPath_BypassesEnforcement()
    {
        SetupSnapshot(SubscriptionStatus.Cancelled, DateTime.UtcNow.AddDays(-21), DateTime.UtcNow.AddDays(-14));
        DefaultHttpContext context = ContextWithTenant(_studioId, "/health", "GET");

        Func<Task> act = () => CreateSut(_ => Task.CompletedTask)
            .InvokeAsync(context, _tenant, _subscriptions);

        await act.Should().NotThrowAsync();
        await _subscriptions.DidNotReceive()
            .GetSnapshotAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private void SetupSnapshot(SubscriptionStatus status, DateTime trialExpiresAt, DateTime gracePeriodEnd) =>
        _subscriptions
            .GetSnapshotAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SubscriptionSnapshot?>(
                new SubscriptionSnapshot(status, trialExpiresAt, gracePeriodEnd)));

    private static DefaultHttpContext ContextWithTenant(
        Guid   studioId,
        string path   = "/api/v1/appointments",
        string method = "GET")
    {
        DefaultHttpContext context = new();
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("tenant_id", studioId.ToString())], "test"));
        context.Request.Path   = path;
        context.Request.Method = method;
        return context;
    }

    private static DefaultHttpContext ContextWithTenantAndRole(
        Guid   studioId,
        string role,
        string path   = "/api/v1/appointments",
        string method = "GET")
    {
        DefaultHttpContext context = new();
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim("tenant_id", studioId.ToString()), new Claim(ClaimTypes.Role, role)],
                "test"));
        context.Request.Path   = path;
        context.Request.Method = method;
        return context;
    }

    private static DefaultHttpContext ContextWithClaim(string type, string value)
    {
        DefaultHttpContext context = new();
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(type, value)], "test"));
        return context;
    }
}
