using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Pena_e_Arte.API.Middleware;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.IntegrationTests.Middleware;

public class TenantMiddlewareTests
{
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();

    private TenantMiddleware CreateSut(RequestDelegate next) => new(next);

    [Fact]
    public async Task InvokeAsync_ValidTenantIdClaim_CallsSetTenantWithCorrectGuid()
    {
        Guid expectedId = Guid.NewGuid();
        DefaultHttpContext context = ContextWithClaim("tenant_id", expectedId.ToString());

        await CreateSut(_ => Task.CompletedTask).InvokeAsync(context, _tenant);

        _tenant.Received(1).SetTenant(expectedId);
    }

    [Fact]
    public async Task InvokeAsync_NoTenantIdClaim_DoesNotCallSetTenant()
    {
        DefaultHttpContext context = new();

        await CreateSut(_ => Task.CompletedTask).InvokeAsync(context, _tenant);

        _tenant.DidNotReceive().SetTenant(Arg.Any<Guid>());
    }

    [Fact]
    public async Task InvokeAsync_InvalidTenantIdClaim_DoesNotCallSetTenant()
    {
        DefaultHttpContext context = ContextWithClaim("tenant_id", "not-a-guid");

        await CreateSut(_ => Task.CompletedTask).InvokeAsync(context, _tenant);

        _tenant.DidNotReceive().SetTenant(Arg.Any<Guid>());
    }

    [Fact]
    public async Task InvokeAsync_EmptyTenantIdClaim_DoesNotCallSetTenant()
    {
        DefaultHttpContext context = ContextWithClaim("tenant_id", "");

        await CreateSut(_ => Task.CompletedTask).InvokeAsync(context, _tenant);

        _tenant.DidNotReceive().SetTenant(Arg.Any<Guid>());
    }

    [Fact]
    public async Task InvokeAsync_ValidTenantId_StillCallsNext()
    {
        DefaultHttpContext context = ContextWithClaim("tenant_id", Guid.NewGuid().ToString());
        bool nextCalled = false;

        await CreateSut(_ => { nextCalled = true; return Task.CompletedTask; }).InvokeAsync(context, _tenant);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_NoClaim_StillCallsNext()
    {
        DefaultHttpContext context = new();
        bool nextCalled = false;

        await CreateSut(_ => { nextCalled = true; return Task.CompletedTask; }).InvokeAsync(context, _tenant);

        nextCalled.Should().BeTrue();
    }

    private static DefaultHttpContext ContextWithClaim(string type, string value)
    {
        DefaultHttpContext context = new();
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(type, value)], "test"));
        return context;
    }
}
