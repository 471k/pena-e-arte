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
        Func<Task> act = () => _sut.CreatePaymentHoldAsync(
            new PaymentHoldRequest(Guid.NewGuid(), Guid.NewGuid(), 1000, "ALL"), default);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CaptureCancelStatusRefund_AllFailClosed()
    {
        Guid studioId = Guid.NewGuid();
        await ((Func<Task>)(() => _sut.CaptureAsync(studioId, "ref", default)))
            .Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(() => _sut.CancelAsync(studioId, "ref", default)))
            .Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(() => _sut.GetStatusAsync(studioId, "ref", default)))
            .Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(() => _sut.RefundAsync(studioId, "ref", null, default)))
            .Should().ThrowAsync<InvalidOperationException>();
    }
}
