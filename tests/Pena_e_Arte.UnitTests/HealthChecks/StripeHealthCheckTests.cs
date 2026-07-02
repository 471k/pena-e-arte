using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using Pena_e_Arte.API.Extensions;
using Stripe;

namespace Pena_e_Arte.UnitTests.HealthChecks;

public class StripeHealthCheckTests
{
    private readonly BalanceService _balanceService = Substitute.For<BalanceService>();

    private StripeHealthCheck CreateSut() => new(_balanceService);

    [Fact]
    public async Task CheckHealthAsync_StripeRespondsSuccessfully_ReturnsHealthy()
    {
        _balanceService.GetAsync(Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(new Balance());

        HealthCheckResult result = await CreateSut().CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_Returns401_ReturnsUnhealthy()
    {
        StripeException exception = new("Unauthorized") { HttpStatusCode = HttpStatusCode.Unauthorized };
        _balanceService.GetAsync(Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns<Balance>(_ => throw exception);

        HealthCheckResult result = await CreateSut().CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().ContainEquivalentOf("invalid or unauthorised");
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsOtherStripeError_ReturnsDegraded()
    {
        StripeException exception = new("Too many requests") { HttpStatusCode = HttpStatusCode.TooManyRequests };
        _balanceService.GetAsync(Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns<Balance>(_ => throw exception);

        HealthCheckResult result = await CreateSut().CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_NetworkError_ReturnsUnhealthy()
    {
        _balanceService.GetAsync(Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns<Balance>(_ => throw new HttpRequestException("network unreachable"));

        HealthCheckResult result = await CreateSut().CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().ContainEquivalentOf("unreachable");
    }

    [Fact]
    public async Task CheckHealthAsync_CancellationRequested_DoesNotReturnHealthy()
    {
        _balanceService.GetAsync(Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns<Balance>(_ => throw new OperationCanceledException());

        HealthCheckResult result = await CreateSut().CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().NotBe(HealthStatus.Healthy);
    }
}
