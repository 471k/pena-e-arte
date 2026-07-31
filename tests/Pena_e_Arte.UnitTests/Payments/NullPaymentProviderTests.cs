using FluentAssertions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.UnitTests.Payments;

public class NullPaymentProviderTests
{
    private readonly IPaymentProvider _sut = new NullPaymentProvider();

    [Fact]
    public void Capabilities_AreAllFalse_SoCapabilityGatedLogicTreatsCardAsUnavailable()
    {
        PaymentProviderCapabilities caps = _sut.Capabilities;

        caps.SupportsSplit.Should().BeFalse();
        caps.SupportsAuthCapture.Should().BeFalse();
        caps.SupportsHoldExpiry.Should().BeFalse();
        caps.SupportedCurrencies.Should().BeEmpty();
    }

    [Fact]
    public async Task CreatePaymentHoldAsync_FailsClosed()
    {
        Func<Task> act = () => _sut.CreatePaymentHoldAsync(1000, "ALL", Guid.NewGuid(), default);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CaptureCancelStatusRefund_AllFailClosed()
    {
        await ((Func<Task>)(() => _sut.CaptureAsync("ref", default)))
            .Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(() => _sut.CancelAsync("ref", default)))
            .Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(() => _sut.GetStatusAsync("ref", default)))
            .Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(() => _sut.RefundAsync("ref", null, default)))
            .Should().ThrowAsync<InvalidOperationException>();
    }
}
